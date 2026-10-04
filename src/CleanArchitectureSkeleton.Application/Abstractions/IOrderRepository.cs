using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Abstractions;

/// <summary>
/// PORT de persistance des commandes. Le repository expose le vocabulaire du MÉTIER
/// (GetById, List...) et non celui de la base (SQL, DbSet) : le service n'a pas à savoir comment on stocke.
/// Un repository par agrégat racine.
/// </summary>
public interface IOrderRepository
{
    /// <summary>Lecture simple, sans suivi de changement, hors transaction. Résiliente (retry + circuit breaker).</summary>
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lecture AVEC intention de modifier. À appeler uniquement dans <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/> :
    /// c'est la transaction qui garantit que personne d'autre ne peut modifier la commande entre cette lecture
    /// et le commit (concurrency control PESSIMISTE : on verrouille d'abord, on travaille ensuite).
    /// </summary>
    Task<Order?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Order> Items, int TotalCount)> ListAsync(
        int page,
        int pageSize,
        OrderStatus? status,
        CancellationToken cancellationToken = default);

    /// <summary>Marque la commande pour insertion. Rien n'est écrit tant que l'unité de travail n'a pas commit.</summary>
    void Add(Order order);

    /// <summary>Marque la commande pour suppression (effective au commit).</summary>
    void Remove(Order order);
}
