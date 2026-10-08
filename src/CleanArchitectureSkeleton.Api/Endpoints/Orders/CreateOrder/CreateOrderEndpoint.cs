using CleanArchitectureSkeleton.Api.Extensions;
using CleanArchitectureSkeleton.Application.Orders;

namespace CleanArchitectureSkeleton.Api.Endpoints;

internal static class CreateOrderEndpoint
{
    public static RouteGroupBuilder MapCreateOrderEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (CreateOrderRequest request, IOrderService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct))
                    .ToHttpResult(order => Results.Created($"/api/orders/{order.Id}", order)))
            .WithName("CreateOrder")
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }
}
