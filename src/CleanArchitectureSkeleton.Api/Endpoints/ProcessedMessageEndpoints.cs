using CleanArchitectureSkeleton.Application.ProcessedMessages;

namespace CleanArchitectureSkeleton.Api.Endpoints;

/// <summary>
/// Endpoint de lecture seule pour l'encart "derniers événements" de l'UI Blazor : expose les lignes de
/// <c>ProcessedMessages</c>, c'est-à-dire les events déjà consommés de façon idempotente par l'<c>OutboxConsumer</c>.
/// Pas de rate limiting dédié : c'est un simple polling de lecture, peu coûteux.
/// </summary>
public static class ProcessedMessageEndpoints
{
    public static IEndpointRouteBuilder MapProcessedMessageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/processed-messages", async (
                IProcessedMessageService service,
                CancellationToken ct,
                int count = 20) =>
                Results.Ok(await service.GetLatestAsync(count, ct)))
            .WithName("ListProcessedMessages")
            .WithTags("ProcessedMessages")
            .Produces<IReadOnlyList<ProcessedMessageDto>>();

        return app;
    }
}
