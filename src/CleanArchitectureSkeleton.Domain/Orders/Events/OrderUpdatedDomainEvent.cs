namespace CleanArchitectureSkeleton.Domain.Orders.Events;

/// <summary>Lev\u00e9 quand le contenu d'une commande (encore en attente) est modifi\u00e9 (<see cref="Order.Update"/>).</summary>
public sealed record OrderUpdatedDomainEvent(Guid OrderId, string CustomerName, decimal Total, DateTimeOffset OccurredOnUtc)
    : Common.IDomainEvent;
