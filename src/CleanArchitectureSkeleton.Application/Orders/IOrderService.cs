using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Orders;

/// <summary>
/// Cas d'usage "Commandes" exposés à la couche de présentation (l'API).
/// Architecture en services classiques (pas de CQRS) : une interface, une classe, des méthodes.
/// Chaque méthode renvoie un <see cref="Result"/> : jamais d'exception pour un cas métier.
/// </summary>
public interface IOrderService
{
    Task<Result<OrderDto>> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);

    Task<Result<OrderDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<OrderDto>>> ListAsync(
        int page, int pageSize, OrderStatus? status, CancellationToken cancellationToken = default);

    Task<Result<OrderDto>> UpdateAsync(Guid id, UpdateOrderRequest request, CancellationToken cancellationToken = default);

    Task<Result<OrderDto>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<OrderDto>> ShipAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<OrderDto>> CancelAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
