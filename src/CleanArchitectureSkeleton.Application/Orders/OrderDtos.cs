using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Orders;

// DTO = Data Transfer Object. On n'expose JAMAIS les entités du Domain à l'extérieur :
// le contrat de l'API (DTO) peut ainsi évoluer indépendamment du modèle métier.

public sealed record OrderLineRequest(string? ProductName, int Quantity, decimal UnitPrice);

public sealed record CreateOrderRequest(string? CustomerName, IReadOnlyList<OrderLineRequest>? Lines);

public sealed record UpdateOrderRequest(string? CustomerName, IReadOnlyList<OrderLineRequest>? Lines);

public sealed record OrderLineDto(string ProductName, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record OrderDto(
    Guid Id,
    string CustomerName,
    OrderStatus Status,
    decimal Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<OrderLineDto> Lines);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
