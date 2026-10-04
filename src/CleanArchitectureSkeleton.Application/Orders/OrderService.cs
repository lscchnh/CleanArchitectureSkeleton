using CleanArchitectureSkeleton.Application.Abstractions;
using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Orders;

/// <summary>
/// Orchestration des cas d'usage. Le service NE CONTIENT PAS de règle métier (elles sont dans <see cref="Order"/>) :
/// il enchaîne simplement  « charger → demander au domaine → persister → invalider le cache ».
/// Il ne dépend que d'abstractions (ports) : testable sans base de données.
/// </summary>
public sealed class OrderService(
    IOrderRepository repository,
    IUnitOfWork unitOfWork,
    ICacheService cache,
    TimeProvider timeProvider) : IOrderService
{
    public const int MaxPageSize = 100;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private static string CacheKey(Guid id) => $"orders:{id:N}";

    public Task<Result<OrderDto>> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default) =>
        // Même une simple création passe par une transaction : l'insertion de la commande et de ses lignes est atomique.
        unitOfWork.ExecuteInTransactionAsync<OrderDto>(_ =>
        {
            var lines = BuildLines(request.Lines);
            if (lines.IsFailure)
            {
                return Task.FromResult<Result<OrderDto>>(lines.Error);
            }

            var order = Order.Create(request.CustomerName, lines.Value, timeProvider.GetUtcNow());
            if (order.IsFailure)
            {
                return Task.FromResult<Result<OrderDto>>(order.Error);
            }

            repository.Add(order.Value);
            return Task.FromResult<Result<OrderDto>>(order.Value.ToDto());
        }, cancellationToken);

    public async Task<Result<OrderDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Cache-aside : on ne va en base que si la commande n'est pas déjà en cache.
        var dto = await cache.GetOrCreateAsync(
            CacheKey(id),
            async ct => (await repository.GetByIdAsync(id, ct))?.ToDto(),
            CacheDuration,
            cancellationToken);

        return dto is null ? OrderErrors.NotFound(id) : dto;
    }

    public async Task<Result<PagedResult<OrderDto>>> ListAsync(
        int page, int pageSize, OrderStatus? status, CancellationToken cancellationToken = default)
    {
        // Les listes ne sont pas mises en cache : trop de combinaisons de clés à invalider.
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (items, total) = await repository.ListAsync(page, pageSize, status, cancellationToken);
        return new PagedResult<OrderDto>(items.Select(o => o.ToDto()).ToList(), page, pageSize, total);
    }

    public Task<Result<OrderDto>> UpdateAsync(Guid id, UpdateOrderRequest request, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, (order, now) =>
        {
            var lines = BuildLines(request.Lines);
            return lines.IsFailure
                ? Result.Failure(lines.Error)
                : order.Update(request.CustomerName, lines.Value, now);
        }, cancellationToken);

    public Task<Result<OrderDto>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, (order, now) => order.Confirm(now), cancellationToken);

    public Task<Result<OrderDto>> ShipAsync(Guid id, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, (order, now) => order.Ship(now), cancellationToken);

    public Task<Result<OrderDto>> CancelAsync(Guid id, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, (order, now) => order.Cancel(now), cancellationToken);

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // Lecture "pour mise à jour" DANS la transaction : la commande est verrouillée jusqu'au commit.
            var order = await repository.GetByIdForUpdateAsync(id, ct);
            if (order is null)
            {
                return Result.Failure(OrderErrors.NotFound(id));
            }

            var deletable = order.EnsureDeletable();
            if (deletable.IsFailure)
            {
                return deletable;
            }

            repository.Remove(order);
            return Result.Success();
        }, cancellationToken);

        if (result.IsSuccess)
        {
            await cache.RemoveAsync(CacheKey(id), cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Squelette commun à toutes les modifications : verrou pessimiste → règle métier → commit → invalidation du cache.
    /// </summary>
    private async Task<Result<OrderDto>> ModifyAsync(
        Guid id,
        Func<Order, DateTimeOffset, Result> mutation,
        CancellationToken cancellationToken)
    {
        var result = await unitOfWork.ExecuteInTransactionAsync<OrderDto>(async ct =>
        {
            var order = await repository.GetByIdForUpdateAsync(id, ct);
            if (order is null)
            {
                return OrderErrors.NotFound(id);
            }

            var outcome = mutation(order, timeProvider.GetUtcNow());
            return outcome.IsFailure ? outcome.Error : order.ToDto();
        }, cancellationToken);

        if (result.IsSuccess)
        {
            // Invalider APRÈS le commit : si on invalidait avant, une lecture concurrente pourrait
            // remettre en cache l'ancienne valeur avant que la transaction ne soit visible.
            await cache.RemoveAsync(CacheKey(id), cancellationToken);
        }

        return result;
    }

    private static Result<List<OrderLine>> BuildLines(IReadOnlyList<OrderLineRequest>? requests)
    {
        var lines = new List<OrderLine>();
        foreach (var request in requests ?? [])
        {
            var line = OrderLine.Create(request.ProductName, request.Quantity, request.UnitPrice);
            if (line.IsFailure)
            {
                return line.Error;
            }

            lines.Add(line.Value);
        }

        return lines;
    }
}
