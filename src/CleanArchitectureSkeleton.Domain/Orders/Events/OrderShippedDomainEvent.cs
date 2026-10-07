namespace CleanArchitectureSkeleton.Domain.Orders.Events;

/// <summary>Lev\u00e9 quand une commande passe \u00e0 l'\u00e9tat <see cref="OrderStatus.Shipped"/> (<see cref="Order.Ship"/>).</summary>
public sealed record OrderShippedDomainEvent(Guid OrderId, DateTimeOffset OccurredOnUtc) : Common.IDomainEvent;
