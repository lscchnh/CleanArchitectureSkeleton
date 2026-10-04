using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Orders;

/// <summary>Conversion Entité du Domain → DTO. Volontairement manuelle : simple, explicite, sans bibliothèque magique.</summary>
internal static class OrderMappings
{
    public static OrderDto ToDto(this Order order) => new(
        order.Id,
        order.CustomerName,
        order.Status,
        order.Total,
        order.CreatedAt,
        order.UpdatedAt,
        order.Lines.Select(l => new OrderLineDto(l.ProductName, l.Quantity, l.UnitPrice, l.LineTotal)).ToList());
}
