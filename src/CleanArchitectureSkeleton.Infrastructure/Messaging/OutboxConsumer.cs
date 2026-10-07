using System.Text;
using CleanArchitectureSkeleton.Infrastructure.Persistence;
using CleanArchitectureSkeleton.Infrastructure.Persistence.Outbox;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchitectureSkeleton.Infrastructure.Messaging;

/// <summary>
/// Consommateur "métier" des events publiés par <see cref="OutboxProcessor"/> sur le topic <c>orders-events</c>.
/// Il simule un système tiers qui réagit aux événements : ici, on se contente de les stocker dans
/// <see cref="ProcessedMessage"/> (table <c>ProcessedMessages</c>) pour pouvoir les afficher dans l'UI Blazor.
///
/// ── Garantie d'ORDRE ──
/// Le topic <c>orders-events</c> est créé avec UNE SEULE partition (voir <see cref="DependencyInjection"/>) et
/// ce consumer est le seul membre de son groupe : Kafka livre les messages d'une partition dans l'ordre
/// strict de leur écriture (offset croissant), à un seul consommateur à la fois. On traite les messages
/// en boucle, un par un, et on ne committe l'offset qu'APRÈS traitement réussi : le prochain message n'est
/// donc jamais traité avant que le précédent soit acquitté.
///
/// ── Garantie d'IDEMPOTENCE ──
/// Kafka promet "au moins une fois" (at-least-once) avec un commit manuel : un même message peut être
/// relu (ex: crash juste après le traitement mais avant le commit de l'offset). On se protège en
/// vérifiant, AVANT d'insérer, si une ligne <c>ProcessedMessages</c> avec ce MessageId existe déjà ;
/// si oui, on committe sans rien refaire.
/// </summary>
internal sealed partial class OutboxConsumer(
    IServiceScopeFactory scopeFactory,
    IConsumer<string, string> consumer,
    ILogger<OutboxConsumer> logger) : BackgroundService
{
    private const string TopicName = "orders-events";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        consumer.Subscribe(TopicName);

        try
        {
            // Boucle de consommation synchrone (bloquante) : volontairement simple pour rester lisible
            // dans un projet pédagogique. Consume() bloque jusqu'au prochain message ou jusqu'à annulation.
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (result is null || result.IsPartitionEOF)
                {
                    continue;
                }

                await HandleMessageAsync(result, stoppingToken);
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task HandleMessageAsync(ConsumeResult<string, string> result, CancellationToken stoppingToken)
    {
        var messageIdHeader = result.Message.Headers.TryGetLastBytes("MessageId", out var bytes)
            ? Encoding.UTF8.GetString(bytes)
            : null;

        if (!Guid.TryParse(messageIdHeader, out var id))
        {
            // Message mal formé (ne devrait pas arriver avec notre propre publisher) : on committe quand même
            // pour ne pas bloquer indéfiniment la lecture de la partition avec un message qu'on ne pourra jamais traiter.
            LogInvalidMessageId(logger, messageIdHeader);
            consumer.Commit(result);
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Idempotence : si on a déjà traité ce message (relecture Kafka après un redémarrage sans commit),
            // on n'insère pas deux fois.
            var alreadyProcessed = await db.ProcessedMessages.AnyAsync(m => m.Id == id, stoppingToken);
            if (!alreadyProcessed)
            {
                db.ProcessedMessages.Add(new ProcessedMessage
                {
                    Id = id,
                    Type = result.Message.Key,
                    Content = result.Message.Value,
                    ProcessedOnUtc = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync(stoppingToken);
                LogProcessed(logger, result.Message.Key, id);
            }
            else
            {
                LogDuplicateIgnored(logger, result.Message.Key, id);
            }

            // Commit APRÈS écriture réussie (ou constat de doublon) : garantit qu'on ne déplace jamais
            // l'offset au-delà d'un message qu'on n'a pas réellement traité.
            consumer.Commit(result);
        }
        catch (Exception ex)
        {
            // Pas de commit : au prochain Consume (après reconnexion/redémarrage), Kafka relivrera ce
            // message depuis le dernier offset committé. Il reste donc le PROCHAIN message traité
            // (pas de perte d'ordre), seulement retardé le temps de corriger le problème.
            LogProcessingFailed(logger, ex, result.Message.Key, id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Event {EventType} ({MessageId}) traité et stocké dans ProcessedMessages.")]
    private static partial void LogProcessed(ILogger logger, string eventType, Guid messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Event {EventType} ({MessageId}) déjà traité, relecture ignorée (idempotence).")]
    private static partial void LogDuplicateIgnored(ILogger logger, string eventType, Guid messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échec de traitement de l'event {EventType} ({MessageId}), nouvelle tentative après redémarrage/reconnexion.")]
    private static partial void LogProcessingFailed(ILogger logger, Exception exception, string eventType, Guid messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message reçu sans MessageId exploitable ({MessageId}) : committé sans traitement.")]
    private static partial void LogInvalidMessageId(ILogger logger, string? messageId);
}
