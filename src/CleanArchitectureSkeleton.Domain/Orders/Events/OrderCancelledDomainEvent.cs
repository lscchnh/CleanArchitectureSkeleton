namespace CleanArchitectureSkeleton.Domain.Orders.Events;

/// <summary>Lev\u00e9 quand une commande passe \u00e0 l'\u00e9tat <see cref="OrderStatus.Cancelled"/> (<see cref="Order.Cancel"/>).</summary>
public sealed record OrderCancelledDomainEvent(Guid OrderId, DateTimeOffset OccurredOnUtc) : Common.IDomainEvent;
