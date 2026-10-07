namespace CleanArchitectureSkeleton.Domain.Orders.Events;

/// <summary>Lev\u00e9 quand une commande est cr\u00e9\u00e9e avec succ\u00e8s (<see cref="Order.Create"/>).</summary>
public sealed record OrderCreatedDomainEvent(Guid OrderId, string CustomerName, decimal Total, DateTimeOffset OccurredOnUtc)
    : Common.IDomainEvent;
