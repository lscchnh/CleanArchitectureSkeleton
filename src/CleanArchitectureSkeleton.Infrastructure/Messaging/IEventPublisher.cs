namespace CleanArchitectureSkeleton.Infrastructure.Messaging;

/// <summary>
/// Abstraction technique (côté Infrastructure, pas Application : c'est un détail de "comment" on simule
/// l'envoi, pas un besoin métier) pour publier un message déjà sérialisé sur le broker configuré.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(string routingKey, string payload, CancellationToken cancellationToken);
}
