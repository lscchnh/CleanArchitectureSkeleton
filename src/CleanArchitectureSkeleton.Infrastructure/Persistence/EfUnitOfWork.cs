using System.Text.Json;
using CleanArchitectureSkeleton.Application.Abstractions;
using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;
using CleanArchitectureSkeleton.Infrastructure.Persistence.Outbox;
using CleanArchitectureSkeleton.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Polly.Registry;

namespace CleanArchitectureSkeleton.Infrastructure.Persistence;

/// <summary>
/// Unité de travail = TRANSACTION + VERROU PESSIMISTE + RÉSILIENCE.
///
/// ── Concurrency control pessimiste ──
/// Principe : "je verrouille AVANT de lire, donc personne ne peut me doubler" (opposé à l'optimiste, qui détecte
/// le conflit APRÈS coup avec un RowVersion et fait échouer le perdant).
/// PostgreSQL verrouille au niveau LIGNE (contrairement à SQLite qui verrouille tout le fichier) :
/// <see cref="OrderRepository"/> exécute un <c>SELECT ... FOR UPDATE</c> dans cette même transaction,
/// ce qui bloque les autres transactions tentant de lire/modifier CETTE ligne, sans impacter les autres commandes.
///
/// ── Transaction ──
/// Tout ce que fait <c>work</c> est atomique : commit si le Result est un succès, rollback sinon (ou en cas d'exception).
///
/// ── Résilience ──
/// Si l'écriture échoue de façon transitoire (connexion PostgreSQL momentanément indisponible, deadlock...),
/// la transaction ENTIÈRE est rejouée par le pipeline Polly.
/// </summary>
internal sealed class EfUnitOfWork(AppDbContext db, ResiliencePipelineProvider<string> pipelines) : IUnitOfWork
{
    private readonly Polly.ResiliencePipeline _pipeline = pipelines.GetPipeline(DatabaseResiliencePipeline.Name);

    public async Task<Result<T>> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default) =>
        await _pipeline.ExecuteAsync(async ct =>
        {
            var result = await RunAsync(async token => await work(token), ct);
            return result;
        }, cancellationToken);

    public async Task<Result> ExecuteInTransactionAsync(
        Func<CancellationToken, Task<Result>> work,
        CancellationToken cancellationToken = default) =>
        await _pipeline.ExecuteAsync(async ct => await RunAsync(work, ct), cancellationToken);

    private async Task<TResult> RunAsync<TResult>(Func<CancellationToken, Task<TResult>> work, CancellationToken ct)
        where TResult : Result
    {
        // Une tentative précédente (échouée) a pu laisser des entités à moitié modifiées dans le DbContext : on repart de zéro.
        db.ChangeTracker.Clear();

        // Transaction standard EF/PostgreSQL. Le verrou pessimiste n'est PAS pris ici : il est pris ligne par ligne
        // par OrderRepository.GetByIdForUpdateAsync (SELECT ... FOR UPDATE) au moment où l'agrégat est lu.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await work(ct);

            if (result.IsFailure)
            {
                // Échec métier : on n'écrit rien (rollback à la sortie du using, la transaction n'est pas commitée).
                return result;
            }

            // Pattern Transactional Outbox : les événements levés par les agrégats modifiés sont copiés
            // dans la table OutboxMessages AVANT SaveChangesAsync, donc dans LA MÊME transaction PostgreSQL
            // que l'écriture métier. Un BackgroundService séparé (OutboxProcessor) les publiera ensuite
            // sur Kafka de façon asynchrone, hors du chemin critique de la requête.
            EnqueueDomainEventsToOutbox();

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        finally
        {
            // Rollback automatique si la transaction n'a pas été commitée (await using Dispose).
        }
    }

    /// <summary>
    /// Parcourt les agrégats <see cref="Order"/> suivis par le ChangeTracker (ajoutés ou modifiés) et transforme
    /// chacun de leurs <c>DomainEvents</c> en une ligne <see cref="OutboxMessage"/>. Le nom du type concret
    /// (ex: "OrderCreatedDomainEvent") est conservé comme discriminant : l'OutboxProcessor n'a donc pas besoin
    /// de connaître le Domain pour router le message, seulement de propager ce nom en clé Kafka.
    /// </summary>
    private void EnqueueDomainEventsToOutbox()
    {
        var orders = db.ChangeTracker.Entries<Order>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity)
            .Where(order => order.DomainEvents.Count > 0)
            .ToList();

        foreach (var order in orders)
        {
            foreach (var domainEvent in order.DomainEvents)
            {
                db.OutboxMessages.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = domainEvent.GetType().Name,
                    Content = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                    OccurredOnUtc = domainEvent.OccurredOnUtc,
                });
            }

            order.ClearDomainEvents();
        }
    }
}
