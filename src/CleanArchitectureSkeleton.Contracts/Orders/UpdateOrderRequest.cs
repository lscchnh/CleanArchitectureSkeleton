namespace CleanArchitectureSkeleton.Contracts.Orders;

public sealed record UpdateOrderRequest(string? CustomerName, IReadOnlyList<OrderLineRequest>? Lines);
