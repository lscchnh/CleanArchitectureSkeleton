using CleanArchitectureSkeleton.Api.Extensions;
using CleanArchitectureSkeleton.Application.Orders;

namespace CleanArchitectureSkeleton.Api.Endpoints;

internal static class CancelOrderEndpoint
{
    public static RouteGroupBuilder MapCancelOrderEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/cancel", async (Guid id, IOrderService service, CancellationToken ct) =>
                (await service.CancelAsync(id, ct)).ToHttpResult(Results.Ok))
            .WithName("CancelOrder");

        return group;
    }
}
