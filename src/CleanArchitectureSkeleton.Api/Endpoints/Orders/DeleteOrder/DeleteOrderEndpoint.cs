using CleanArchitectureSkeleton.Api.Extensions;
using CleanArchitectureSkeleton.Application.Orders;

namespace CleanArchitectureSkeleton.Api.Endpoints;

internal static class DeleteOrderEndpoint
{
    public static RouteGroupBuilder MapDeleteOrderEndpoint(this RouteGroupBuilder group)
    {
        group.MapDelete("/{id:guid}", async (Guid id, IOrderService service, CancellationToken ct) =>
                (await service.DeleteAsync(id, ct)).ToHttpResult(() => Results.NoContent()))
            .WithName("DeleteOrder")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return group;
    }
}
