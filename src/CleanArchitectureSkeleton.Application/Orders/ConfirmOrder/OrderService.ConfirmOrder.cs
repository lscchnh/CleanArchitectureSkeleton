using CleanArchitectureSkeleton.Domain.Common;

namespace CleanArchitectureSkeleton.Application.Orders;

public sealed partial class OrderService
{
    public Task<Result<OrderDto>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, (order, now) => order.Confirm(now), cancellationToken);
}
