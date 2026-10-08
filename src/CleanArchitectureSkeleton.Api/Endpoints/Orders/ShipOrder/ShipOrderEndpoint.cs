using CleanArchitectureSkeleton.Api.Extensions;
using CleanArchitectureSkeleton.Application.Orders;

namespace CleanArchitectureSkeleton.Api.Endpoints;

internal static class ShipOrderEndpoint
{
    public static RouteGroupBuilder MapShipOrderEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/ship", async (Guid id, IOrderService service, CancellationToken ct) =>
                (await service.ShipAsync(id, ct)).ToHttpResult(Results.Ok))
            .WithName("ShipOrder");

        return group;
    }
}
