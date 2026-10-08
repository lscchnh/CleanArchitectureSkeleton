using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Orders;

public sealed partial class OrderService
{
    public async Task<Result<PagedResult<OrderDto>>> ListAsync(
        int page, int pageSize, OrderStatus? status, CancellationToken cancellationToken = default)
    {
        // Les listes ne sont pas mises en cache : trop de combinaisons de clés à invalider.
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (items, total) = await _repository.ListAsync(page, pageSize, status, cancellationToken);
        return new PagedResult<OrderDto>(items.Select(o => o.ToDto()).ToList(), page, pageSize, total);
    }
}
