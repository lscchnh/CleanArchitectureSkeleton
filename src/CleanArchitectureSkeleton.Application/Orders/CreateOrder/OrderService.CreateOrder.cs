using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Orders;

public sealed partial class OrderService
{
    public Task<Result<OrderDto>> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default) =>
        // Même une simple création passe par une transaction : l'insertion de la commande et de ses lignes est atomique.
        _unitOfWork.ExecuteInTransactionAsync<OrderDto>(_ =>
        {
            var lines = BuildLines(request.Lines);
            if (lines.IsFailure)
            {
                return Task.FromResult<Result<OrderDto>>(lines.Error);
            }

            var order = Order.Create(request.CustomerName, lines.Value, _timeProvider.GetUtcNow());
            if (order.IsFailure)
            {
                return Task.FromResult<Result<OrderDto>>(order.Error);
            }

            _repository.Add(order.Value);
            return Task.FromResult<Result<OrderDto>>(order.Value.ToDto());
        }, cancellationToken);
}
