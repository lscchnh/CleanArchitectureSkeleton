using CleanArchitectureSkeleton.Api.Extensions;
using CleanArchitectureSkeleton.Application.Orders;

namespace CleanArchitectureSkeleton.Api.Endpoints;

internal static class UpdateOrderEndpoint
{
    public static RouteGroupBuilder MapUpdateOrderEndpoint(this RouteGroupBuilder group)
    {
        group.MapPut("/{id:guid}", async (Guid id, UpdateOrderRequest request, IOrderService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttpResult(Results.Ok))
            .WithName("UpdateOrder")
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return group;
    }
}
