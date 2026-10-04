using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace CleanArchitectureSkeleton.Api.Middleware;

/// <summary>
/// Filet de sécurité pour les EXCEPTIONS (pannes, bugs) — les erreurs métier, elles, passent par le pattern Result.
/// Transforme chaque exception en ProblemDetails propre, sans jamais divulguer la stack trace au client.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // Le client a simplement abandonné sa requête : ce n'est pas une erreur serveur.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        ProblemDetails problem;
        switch (exception)
        {
            // Circuit breaker ouvert : la base est malade, on rejette tout de suite (fail fast) en 503
            // et on indique au client quand réessayer. Un load balancer peut alors router ailleurs.
            case BrokenCircuitException:
                logger.LogWarning("Circuit breaker ouvert : requête rejetée.");
                // Indicatif : aligné sur la BreakDuration par défaut du circuit (15 s).
                httpContext.Response.Headers.RetryAfter = "15";
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = "Database.Unavailable",
                    Detail = "Le service est temporairement indisponible. Réessayez plus tard.",
                };
                break;

            case TimeoutRejectedException:
                logger.LogWarning(exception, "Timeout d'accès à la base de données.");
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = "Database.Timeout",
                    Detail = "Le service met trop de temps à répondre. Réessayez plus tard.",
                };
                break;

            default:
                logger.LogError(exception, "Exception non gérée.");
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Server.Error",
                    Detail = "Une erreur inattendue est survenue.",
                };
                break;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
