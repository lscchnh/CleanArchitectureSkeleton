namespace CleanArchitectureSkeleton.Infrastructure.Messaging;

/// <summary>
/// Abstraction technique (côté Infrastructure, pas Application : c'est un détail de "comment" on simule
/// l'envoi, pas un besoin métier) pour publier un message déjà sérialisé sur le broker configuré.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// <paramref name="messageId"/> est propagé tel quel dans les propriétés AMQP (MessageId) : c'est la clé
    /// que l'OutboxConsumer réutilise ensuite pour garantir l'idempotence côté ProcessedMessages.
    /// </summary>
    Task PublishAsync(Guid messageId, string routingKey, string payload, CancellationToken cancellationToken);
}
