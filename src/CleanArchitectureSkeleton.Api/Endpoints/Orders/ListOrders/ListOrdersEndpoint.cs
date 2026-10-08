using CleanArchitectureSkeleton.Api.Extensions;
using CleanArchitectureSkeleton.Application.Orders;
using CleanArchitectureSkeleton.Domain.Orders;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureSkeleton.Api.Endpoints;

internal static class ListOrdersEndpoint
{
    public static RouteGroupBuilder MapListOrdersEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (
                    IOrderService service,
                    CancellationToken ct,
                    [FromQuery] int page = 1,
                    [FromQuery] int pageSize = 20,
                    [FromQuery] OrderStatus? status = null) =>
                (await service.ListAsync(page, pageSize, status, ct)).ToHttpResult(Results.Ok))
            .WithName("ListOrders")
            .Produces<PagedResult<OrderDto>>();

        return group;
    }
}
