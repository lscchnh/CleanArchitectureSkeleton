using CleanArchitectureSkeleton.Domain.Common;

namespace CleanArchitectureSkeleton.Application.Orders;

public sealed partial class OrderService
{
    public Task<Result<OrderDto>> CancelAsync(Guid id, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, (order, now) => order.Cancel(now), cancellationToken);
}
