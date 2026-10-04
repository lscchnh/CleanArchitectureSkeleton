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
        // Le verrou a déjà été posé au démarrage de la transaction (BEGIN IMMEDIATE) : cette lecture est donc protégée.
        // Sur PostgreSQL/SQL Server, on écrirait ici un verrou de ligne : SELECT ... FOR UPDATE / UPDLOCK.
        await db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

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
