using CleanArchitectureSkeleton.Application.Abstractions;
using CleanArchitectureSkeleton.Application.ProcessedMessages;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitectureSkeleton.Infrastructure.Persistence;

/// <summary>
/// Adaptateur EF Core de <see cref="IProcessedMessageQueries"/> : une simple lecture, hors transaction,
/// comme <c>IOrderRepository.GetByIdAsync</c> (pas besoin du verrou pessimiste de <c>EfUnitOfWork</c> ici,
/// on ne fait que LIRE pour affichage).
/// </summary>
internal sealed class ProcessedMessageQueries(AppDbContext db) : IProcessedMessageQueries
{
    public async Task<IReadOnlyList<ProcessedMessageDto>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        await db.ProcessedMessages
            .AsNoTracking()
            .OrderByDescending(m => m.ProcessedOnUtc)
            .Take(count)
            .Select(m => new ProcessedMessageDto(m.Id, m.Type, m.Content, m.ProcessedOnUtc))
            .ToListAsync(cancellationToken);
}
