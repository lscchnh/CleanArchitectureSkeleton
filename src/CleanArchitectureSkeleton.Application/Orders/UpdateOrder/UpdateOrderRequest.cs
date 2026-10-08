namespace CleanArchitectureSkeleton.Application.Orders;

public sealed record UpdateOrderRequest(string? CustomerName, IReadOnlyList<OrderLineRequest>? Lines);
