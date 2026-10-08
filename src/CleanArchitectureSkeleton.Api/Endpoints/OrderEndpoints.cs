using CleanArchitectureSkeleton.Api.RateLimiting;

namespace CleanArchitectureSkeleton.Api.Endpoints;

/// <summary>
/// Couche Présentation : Minimal API. Organisée en VERTICAL SLICES (<c>Orders/&lt;CasDUsage&gt;/...Endpoint.cs</c>) :
/// chaque cas d'usage possède son propre fichier, au même endroit logique que sa slice Application correspondante.
/// Ce fichier ne fait plus que composer le groupe de routes et y attacher chaque slice.
/// Les endpoints restent FINS : ils extraient les paramètres HTTP, délèguent au service applicatif,
/// puis traduisent le Result en réponse HTTP. Aucune logique métier ici.
/// </summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders")
            .WithTags("Orders")
            // Rate limiting (token bucket) appliqué à tout le groupe.
            .RequireRateLimiting(RateLimitingExtensions.OrdersPolicy);

        group.MapCreateOrderEndpoint();
        group.MapGetOrderEndpoint();
        group.MapListOrdersEndpoint();
        group.MapUpdateOrderEndpoint();
        group.MapConfirmOrderEndpoint();
        group.MapShipOrderEndpoint();
        group.MapCancelOrderEndpoint();
        group.MapDeleteOrderEndpoint();

        return app;
    }
}
