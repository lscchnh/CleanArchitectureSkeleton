namespace CleanArchitectureSkeleton.Application.ProcessedMessages;

/// <summary>
/// Cas d'usage "derniers événements traités" exposé à la couche de présentation (l'Api).
/// Même convention que <see cref="Orders.IOrderService"/> : l'Api ne parle qu'à des services applicatifs,
/// jamais directement aux ports/repositories (<see cref="Abstractions.IProcessedMessageQueries"/>).
/// </summary>
public interface IProcessedMessageService
{
    /// <summary>Les <paramref name="count"/> événements les plus récemment traités, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<ProcessedMessageDto>> GetLatestAsync(int count, CancellationToken cancellationToken = default);
}
