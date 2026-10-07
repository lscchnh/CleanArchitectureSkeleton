namespace CleanArchitectureSkeleton.Domain.Orders.Events;

/// <summary>Lev\u00e9 quand une commande passe \u00e0 l'\u00e9tat <see cref="OrderStatus.Confirmed"/> (<see cref="Order.Confirm"/>).</summary>
public sealed record OrderConfirmedDomainEvent(Guid OrderId, DateTimeOffset OccurredOnUtc) : Common.IDomainEvent;
