using CleanArchitectureSkeleton.Api.Extensions;
using CleanArchitectureSkeleton.Application.Orders;

namespace CleanArchitectureSkeleton.Api.Endpoints;

// Actions métier modélisées en sous-ressource POST (plutôt qu'un PATCH du champ "status") :
// elle expose l'intention, et le Domain contrôle les transitions autorisées.
internal static class ConfirmOrderEndpoint
{
    public static RouteGroupBuilder MapConfirmOrderEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/confirm", async (Guid id, IOrderService service, CancellationToken ct) =>
                (await service.ConfirmAsync(id, ct)).ToHttpResult(Results.Ok))
            .WithName("ConfirmOrder");

        return group;
    }
}
