namespace CleanArchitectureSkeleton.Application.Orders;

public sealed record CreateOrderRequest(string? CustomerName, IReadOnlyList<OrderLineRequest>? Lines);
