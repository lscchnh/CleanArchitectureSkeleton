using CleanArchitectureSkeleton.Application.Abstractions;

namespace CleanArchitectureSkeleton.Application.ProcessedMessages;

/// <summary>
/// Simple délégation vers le port de lecture : il n'y a aucune orchestration à faire ici (une seule requête,
/// hors transaction), mais le service existe quand même pour que l'Api ne dépende jamais d'un port directement.
/// </summary>
public sealed class ProcessedMessageService(IProcessedMessageQueries queries) : IProcessedMessageService
{
    public Task<IReadOnlyList<ProcessedMessageDto>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        queries.GetLatestAsync(count, cancellationToken);
}
