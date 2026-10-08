namespace CleanArchitectureSkeleton.Application.Orders;

// Ligne en ÉCRITURE, partagée par les deux seules slices qui en ont besoin : CreateOrder et UpdateOrder.
public sealed record OrderLineRequest(string? ProductName, int Quantity, decimal UnitPrice);
