using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Orders;

public sealed partial class OrderService
{
    public async Task<Result<OrderDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Cache-aside : on ne va en base que si la commande n'est pas déjà en cache.
        var dto = await _cache.GetOrCreateAsync(
            CacheKey(id),
            async ct => (await _repository.GetByIdAsync(id, ct))?.ToDto(),
            CacheDuration,
            cancellationToken);

        return dto is null ? OrderErrors.NotFound(id) : dto;
    }
}
