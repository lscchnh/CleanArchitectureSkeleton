using CleanArchitectureSkeleton.Application.Abstractions;
using CleanArchitectureSkeleton.Domain.Orders;
using CleanArchitectureSkeleton.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Polly.Registry;

namespace CleanArchitectureSkeleton.Infrastructure.Persistence;

/// <summary>Implémentation EF Core du port <see cref="IOrderRepository"/>.</summary>
internal sealed class OrderRepository(AppDbContext db, ResiliencePipelineProvider<string> pipelines) : IOrderRepository
{
    private readonly Polly.ResiliencePipeline _pipeline = pipelines.GetPipeline(DatabaseResiliencePipeline.Name);

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        // Lecture seule : AsNoTracking évite le coût du suivi de changements.
        // Réessayer une simple lecture est toujours sûr (idempotent), contrairement à une écriture isolée.
        await _pipeline.ExecuteAsync(
            async ct => await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct),
            cancellationToken);

    public async Task<Order?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default) =>
        // PAS de pipeline ici : on est dans une transaction, et c'est la transaction ENTIÈRE qui est rejouée
        // par EfUnitOfWork. Réessayer une seule requête au milieu d'une transaction fausserait la logique.
        // FOR UPDATE pose un verrou EXCLUSIF sur CETTE ligne, pour la durée de la transaction en cours :
        // toute autre transaction qui tente de la lire avec FOR UPDATE (ou de la modifier) est mise en attente
        // jusqu'au commit/rollback de celle-ci. Les lectures simples (sans FOR UPDATE, ex: GetByIdAsync) ne
        // sont PAS bloquées : seul le "pessimistic write lock" est concerné.
        await db.Orders
            .FromSqlInterpolated($"""SELECT * FROM "Orders" WHERE "Id" = {id} FOR UPDATE""")
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<Order> Items, int TotalCount)> ListAsync(
        int page, int pageSize, OrderStatus? status, CancellationToken cancellationToken = default)
    {
        return await _pipeline.ExecuteAsync(async ct =>
        {
            var query = db.Orders.AsNoTracking();
            if (status is not null)
            {
                query = query.Where(o => o.Status == status);
            }

            var total = await query.CountAsync(ct);

            // Tri déterministe (date puis Id) indispensable pour une pagination stable.
            var items = await query
                .OrderByDescending(o => o.CreatedAt)
                .ThenBy(o => o.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return ((IReadOnlyList<Order>)items, total);
        }, cancellationToken);
    }

    public void Add(Order order) => db.Orders.Add(order);

    public void Remove(Order order) => db.Orders.Remove(order);
}
