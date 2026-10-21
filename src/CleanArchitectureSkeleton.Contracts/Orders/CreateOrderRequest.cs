namespace CleanArchitectureSkeleton.Contracts.Orders;

public sealed record CreateOrderRequest(string? CustomerName, IReadOnlyList<OrderLineRequest>? Lines);
