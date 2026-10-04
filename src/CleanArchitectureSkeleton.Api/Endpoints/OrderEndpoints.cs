using CleanArchitectureSkeleton.Api.Extensions;
using CleanArchitectureSkeleton.Api.RateLimiting;
using CleanArchitectureSkeleton.Application.Orders;
using CleanArchitectureSkeleton.Domain.Orders;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureSkeleton.Api.Endpoints;

/// <summary>
/// Couche Présentation : Minimal API. Les endpoints sont FINS : ils extraient les paramètres HTTP,
/// délèguent au service applicatif, puis traduisent le Result en réponse HTTP. Aucune logique métier ici.
/// </summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders")
            .WithTags("Orders")
            // Rate limiting (token bucket) appliqué à tout le groupe.
            .RequireRateLimiting(RateLimitingExtensions.OrdersPolicy);

        group.MapPost("/", async (CreateOrderRequest request, IOrderService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct))
                    .ToHttpResult(order => Results.Created($"/api/orders/{order.Id}", order)))
            .WithName("CreateOrder")
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", async (Guid id, IOrderService service, CancellationToken ct) =>
                (await service.GetByIdAsync(id, ct)).ToHttpResult(Results.Ok))
            .WithName("GetOrder")
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
                    IOrderService service,
                    CancellationToken ct,
                    [FromQuery] int page = 1,
                    [FromQuery] int pageSize = 20,
                    [FromQuery] OrderStatus? status = null) =>
                (await service.ListAsync(page, pageSize, status, ct)).ToHttpResult(Results.Ok))
            .WithName("ListOrders")
            .Produces<PagedResult<OrderDto>>();

        group.MapPut("/{id:guid}", async (Guid id, UpdateOrderRequest request, IOrderService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttpResult(Results.Ok))
            .WithName("UpdateOrder")
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Actions métier modélisées en sous-ressources POST (plutôt qu'un PATCH du champ "status") :
        // elles exposent l'intention, et le Domain contrôle les transitions autorisées.
        group.MapPost("/{id:guid}/confirm", async (Guid id, IOrderService service, CancellationToken ct) =>
                (await service.ConfirmAsync(id, ct)).ToHttpResult(Results.Ok))
            .WithName("ConfirmOrder");

        group.MapPost("/{id:guid}/ship", async (Guid id, IOrderService service, CancellationToken ct) =>
                (await service.ShipAsync(id, ct)).ToHttpResult(Results.Ok))
            .WithName("ShipOrder");

        group.MapPost("/{id:guid}/cancel", async (Guid id, IOrderService service, CancellationToken ct) =>
                (await service.CancelAsync(id, ct)).ToHttpResult(Results.Ok))
            .WithName("CancelOrder");

        group.MapDelete("/{id:guid}", async (Guid id, IOrderService service, CancellationToken ct) =>
                (await service.DeleteAsync(id, ct)).ToHttpResult(() => Results.NoContent()))
            .WithName("DeleteOrder")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
