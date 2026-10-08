using CleanArchitectureSkeleton.Domain.Common;

namespace CleanArchitectureSkeleton.Application.Orders;

public sealed partial class OrderService
{
    public Task<Result<OrderDto>> UpdateAsync(Guid id, UpdateOrderRequest request, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, (order, now) =>
        {
            var lines = BuildLines(request.Lines);
            return lines.IsFailure
                ? Result.Failure(lines.Error)
                : order.Update(request.CustomerName, lines.Value, now);
        }, cancellationToken);
}
