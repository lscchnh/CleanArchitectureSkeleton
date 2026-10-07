using CleanArchitectureSkeleton.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchitectureSkeleton.Infrastructure.Messaging;

/// <summary>
/// Second temps du pattern Outbox : un <see cref="BackgroundService"/> qui, toutes les <see cref="PollingInterval"/>,
/// lit un lot de <c>OutboxMessages</c> non traités, les publie sur RabbitMQ (via <see cref="IEventPublisher"/>),
/// puis marque <c>ProcessedOnUtc</c>. C'est volontairement du "at-least-once" + relecture simple :
/// si la publication échoue, le message reste non traité et sera retenté au prochain tick
/// (pas de DLQ ni de backoff exponentiel ici, pour rester lisible dans un projet pédagogique).
/// </summary>
internal sealed partial class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollingInterval);

        // On traite un lot dès le démarrage, puis à chaque tick du timer.
        do
        {
            await ProcessPendingMessagesAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        // Un scope dédié : AppDbContext est enregistré "par requête" (AddDbContext), ce BackgroundService
        // doit donc créer lui-même son propre scope de vie courte à chaque passage.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pendingMessages = await db.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (pendingMessages.Count == 0)
        {
            return;
        }

        foreach (var message in pendingMessages)
        {
            try
            {
                // Routing key = type de l'événement (ex: "OrderCreatedDomainEvent") : voir RabbitMqEventPublisher.
                // messageId = Id de ce message Outbox : propagé jusqu'au consumer pour l'idempotence (ProcessedMessages).
                await publisher.PublishAsync(message.Id, message.Type, message.Content, cancellationToken);
                message.ProcessedOnUtc = DateTimeOffset.UtcNow;
                message.Error = null;
                LogPublished(logger, message.Type, message.Id);
            }
            catch (Exception ex)
            {
                // On n'interrompt pas le lot : un message en échec ne doit pas bloquer les suivants.
                message.Error = ex.Message;
                LogPublishFailed(logger, ex, message.Type, message.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // LoggerMessage source-generated : plus performant qu'un appel direct LogInformation/LogWarning
    // (pas de boxing des arguments quand le niveau de log est désactivé).
    [LoggerMessage(Level = LogLevel.Information, Message = "Event {EventType} ({MessageId}) publié sur RabbitMQ.")]
    private static partial void LogPublished(ILogger logger, string eventType, Guid messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échec de publication de l'event {EventType} ({MessageId}), nouvelle tentative au prochain tick.")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, string eventType, Guid messageId);
}
