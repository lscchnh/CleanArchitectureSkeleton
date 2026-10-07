using System.Text.Json;
using CleanArchitectureSkeleton.Application.Abstractions;
using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;
using CleanArchitectureSkeleton.Infrastructure.Persistence.Outbox;
using CleanArchitectureSkeleton.Infrastructure.Resilience;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Polly.Registry;

namespace CleanArchitectureSkeleton.Infrastructure.Persistence;

/// <summary>
/// Unité de travail = TRANSACTION + VERROU PESSIMISTE + RÉSILIENCE.
///
/// ── Concurrency control pessimiste ──
/// Principe : "je verrouille AVANT de lire, donc personne ne peut me doubler" (opposé à l'optimiste, qui détecte
/// le conflit APRÈS coup avec un RowVersion et fait échouer le perdant).
/// SQLite n'a pas de verrou de ligne (pas de SELECT ... FOR UPDATE) : son équivalent est
/// <c>BEGIN IMMEDIATE</c>, qui prend le verrou d'ÉCRITURE de la base dès l'ouverture de la transaction.
/// Les autres écrivains attendent (busy timeout) puis passent à leur tour : les modifications sont sérialisées,
/// ce qui élimine les "lost updates" (deux utilisateurs qui écrasent mutuellement leurs changements).
/// Pourquoi pas le BEGIN par défaut (DEFERRED) ? Il démarre en lecture et tente de passer en écriture plus tard :
/// si un autre écrivain est passé entre-temps, SQLite échoue immédiatement avec SQLITE_BUSY (deadlock de mise à niveau).
/// Verrouiller dès le début évite ce piège.
/// Avec WAL (activé au démarrage), les lecteurs ne sont jamais bloqués par ce verrou.
///
/// ── Transaction ──
/// Tout ce que fait <c>work</c> est atomique : commit si le Result est un succès, rollback sinon (ou en cas d'exception).
///
/// ── Résilience ──
/// Si l'écriture échoue de façon transitoire (base occupée...), la transaction ENTIÈRE est rejouée par le pipeline Polly.
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

        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        try
        {
            // deferred: false  ⇒  BEGIN IMMEDIATE  ⇒  verrou d'écriture pris tout de suite (pessimiste).
            await using var transaction = connection.BeginTransaction(deferred: false);

            // On dit à EF Core de participer à CETTE transaction (celle qui détient le verrou).
            await db.Database.UseTransactionAsync(transaction, ct);
            try
            {
                var result = await work(ct);

                if (result.IsFailure)
                {
                    // Échec métier : on n'écrit rien (rollback à la sortie du using, la transaction n'est pas commitée).
                    return result;
                }

                // Pattern Transactional Outbox : les événements levés par les agrégats modifiés sont copiés
                // dans la table OutboxMessages AVANT SaveChangesAsync, donc dans LA MÊME transaction SQLite
                // que l'écriture métier. Un BackgroundService séparé (OutboxProcessor) les publiera ensuite
                // sur RabbitMQ de façon asynchrone, hors du chemin critique de la requête.
                EnqueueDomainEventsToOutbox();

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            finally
            {
                // On détache la transaction du DbContext pour qu'il reste réutilisable (retry, appels suivants).
                await db.Database.UseTransactionAsync(null, CancellationToken.None);
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Parcourt les agrégats <see cref="Order"/> suivis par le ChangeTracker (ajoutés ou modifiés) et transforme
    /// chacun de leurs <c>DomainEvents</c> en une ligne <see cref="OutboxMessage"/>. Le nom du type concret
    /// (ex: "OrderCreatedDomainEvent") est conservé comme discriminant : l'OutboxProcessor n'a donc pas besoin
    /// de connaître le Domain pour router le message, seulement de propager ce nom en routing key RabbitMQ.
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
