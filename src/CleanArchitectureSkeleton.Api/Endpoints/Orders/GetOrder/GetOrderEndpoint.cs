using CleanArchitectureSkeleton.Api.Extensions;
using CleanArchitectureSkeleton.Application.Orders;

namespace CleanArchitectureSkeleton.Api.Endpoints;

internal static class GetOrderEndpoint
{
    public static RouteGroupBuilder MapGetOrderEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", async (Guid id, IOrderService service, CancellationToken ct) =>
                (await service.GetByIdAsync(id, ct)).ToHttpResult(Results.Ok))
            .WithName("GetOrder")
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }
}
