using CleanArchitectureSkeleton.Domain.Common;

namespace CleanArchitectureSkeleton.Application.Orders;

public sealed partial class OrderService
{
    public Task<Result<OrderDto>> ShipAsync(Guid id, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, (order, now) => order.Ship(now), cancellationToken);
}
