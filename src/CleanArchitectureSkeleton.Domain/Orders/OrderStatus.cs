namespace CleanArchitectureSkeleton.Domain.Orders;

/// <summary>
/// Cycle de vie d'une commande :
/// Pending → Confirmed → Shipped, ou Pending/Confirmed → Cancelled.
/// Les règles de transition sont portées par l'agrégat <see cref="Order"/>.
/// </summary>
public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Shipped = 2,
    Cancelled = 3,
}
