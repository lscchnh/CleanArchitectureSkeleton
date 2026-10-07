using System.Text;
using CleanArchitectureSkeleton.Infrastructure.Persistence;
using CleanArchitectureSkeleton.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CleanArchitectureSkeleton.Infrastructure.Messaging;

/// <summary>
/// Consommateur "métier" des events publiés par <see cref="OutboxProcessor"/> sur l'exchange <c>orders-events</c>.
/// Il simule un système tiers qui réagit aux événements : ici, on se contente de les stocker dans
/// <see cref="ProcessedMessage"/> (table <c>ProcessedMessages</c>) pour pouvoir les afficher dans l'UI Blazor.
///
/// ── Garantie d'ORDRE ──
/// Une seule queue durable (<c>orders-events-queue</c>), liée à l'exchange avec la routing key "#" (tout reçoit).
/// RabbitMQ livre les messages d'une queue à un unique consommateur strictement dans l'ordre d'arrivée (FIFO).
/// On force <c>BasicQosAsync(prefetchCount: 1)</c> : le broker n'envoie le message suivant qu'une fois le
/// précédent acquitté (ack). Sans cela, plusieurs messages pourraient être "in flight" en parallèle et être
/// traités dans le désordre (ex: si le traitement du 2nd se termine avant celui du 1er).
///
/// ── Garantie d'IDEMPOTENCE ──
/// RabbitMQ promet "au moins une fois" : un même message peut être redélivré (ex: crash juste après le
/// traitement mais avant l'envoi de l'ack). On se protège en vérifiant, AVANT d'insérer, si une ligne
/// <c>ProcessedMessages</c> avec ce MessageId existe déjà ; si oui, on acquitte sans rien refaire.
/// </summary>
internal sealed partial class OutboxConsumer(
    IServiceScopeFactory scopeFactory,
    IConnection connection,
    ILogger<OutboxConsumer> logger) : BackgroundService
{
    private const string ExchangeName = "orders-events";
    private const string QueueName = "orders-events-queue";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
        await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        // "#" = tous les événements, quel que soit leur type (routing key). Un vrai système multi-consommateurs
        // pourrait filtrer (ex: "OrderShipped*") pour ne s'abonner qu'à ce qui l'intéresse.
        await channel.QueueBindAsync(QueueName, ExchangeName, routingKey: "#", cancellationToken: stoppingToken);

        // prefetchCount: 1 ⇒ un seul message "en vol" à la fois ⇒ traitement strictement séquentiel ⇒ ordre garanti.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) => await HandleMessageAsync(channel, eventArgs, stoppingToken);

        // autoAck: false ⇒ c'est NOUS qui acquittons explicitement, seulement après écriture réussie en base
        // (voir HandleMessageAsync) : pas de perte de message si le process s'arrête en cours de traitement.
        await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, cancellationToken: stoppingToken);

        // Le BackgroundService doit rester vivant tant que l'hôte tourne ; le vrai travail se fait dans ReceivedAsync.
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => Task.CompletedTask, TaskScheduler.Default);
    }

    private async Task HandleMessageAsync(IChannel channel, BasicDeliverEventArgs eventArgs, CancellationToken stoppingToken)
    {
        var messageId = eventArgs.BasicProperties.MessageId;
        if (!Guid.TryParse(messageId, out var id))
        {
            // Message mal formé (ne devrait pas arriver avec notre propre publisher) : on l'acquitte quand même
            // pour ne pas bloquer la queue indéfiniment avec un message qu'on ne pourra jamais traiter.
            LogInvalidMessageId(logger, messageId);
            await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, stoppingToken);
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Idempotence : si on a déjà traité ce message (redélivraison RabbitMQ), on n'insère pas deux fois.
            var alreadyProcessed = await db.ProcessedMessages.AnyAsync(m => m.Id == id, stoppingToken);
            if (!alreadyProcessed)
            {
                db.ProcessedMessages.Add(new ProcessedMessage
                {
                    Id = id,
                    Type = eventArgs.RoutingKey,
                    Content = Encoding.UTF8.GetString(eventArgs.Body.Span),
                    ProcessedOnUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync(stoppingToken);
                LogProcessed(logger, eventArgs.RoutingKey, id);
            }
            else
            {
                LogDuplicateIgnored(logger, eventArgs.RoutingKey, id);
            }

            // Ack APRÈS écriture réussie (ou constat de doublon) : garantit qu'on n'acquitte jamais un message
            // qu'on n'a pas réellement traité.
            await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex)
        {
            LogProcessingFailed(logger, ex, eventArgs.RoutingKey, id);
            // requeue: true ⇒ le message retourne en tête de la queue et sera redistribué : il reste donc
            // le PROCHAIN message traité (pas de perte d'ordre), seulement retardé le temps de corriger le problème.
            await channel.BasicNackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Event {EventType} ({MessageId}) traité et stocké dans ProcessedMessages.")]
    private static partial void LogProcessed(ILogger logger, string eventType, Guid messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Event {EventType} ({MessageId}) déjà traité, redélivraison ignorée (idempotence).")]
    private static partial void LogDuplicateIgnored(ILogger logger, string eventType, Guid messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échec de traitement de l'event {EventType} ({MessageId}), remis en queue pour nouvelle tentative.")]
    private static partial void LogProcessingFailed(ILogger logger, Exception exception, string eventType, Guid messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message reçu sans MessageId exploitable ({MessageId}) : acquitté sans traitement.")]
    private static partial void LogInvalidMessageId(ILogger logger, string? messageId);
}
