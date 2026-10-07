namespace CleanArchitectureSkeleton.Application.Abstractions;

/// <summary>
/// PORT de lecture (requête simple, hors transaction) des events déjà consommés par l'<c>OutboxConsumer</c>.
/// Utilisé uniquement pour l'affichage (encart "derniers événements" côté Blazor) : pas de logique métier ici,
/// donc pas de méthode d'écriture sur ce port (l'écriture se fait dans l'Infrastructure, côté consumer).
/// </summary>
public interface IProcessedMessageQueries
{
    /// <summary>Les <paramref name="count"/> événements les plus récemment traités, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<ProcessedMessages.ProcessedMessageDto>> GetLatestAsync(int count, CancellationToken cancellationToken = default);
}
