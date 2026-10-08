using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Orders;

public sealed partial class OrderService
{
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // Lecture "pour mise à jour" DANS la transaction : la commande est verrouillée jusqu'au commit.
            var order = await _repository.GetByIdForUpdateAsync(id, ct);
            if (order is null)
            {
                return Result.Failure(OrderErrors.NotFound(id));
            }

            var deletable = order.EnsureDeletable();
            if (deletable.IsFailure)
            {
                return deletable;
            }

            _repository.Remove(order);
            return Result.Success();
        }, cancellationToken);

        if (result.IsSuccess)
        {
            await _cache.RemoveAsync(CacheKey(id), cancellationToken);
        }

        return result;
    }
}
