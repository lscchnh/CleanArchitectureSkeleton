using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly.CircuitBreaker;

namespace CleanArchitectureSkeleton.Infrastructure.HealthChecks;

/// <summary>
/// Rend l'état du circuit breaker observable par l'orchestrateur et la supervision :
///  Closed → Healthy · HalfOpen → Degraded · Open → Degraded (l'instance reste vivante mais dégradée).
/// On renvoie Degraded et non Unhealthy : un Unhealthy pourrait faire redémarrer le pod en boucle,
/// ce qui n'aiderait en rien une base de données défaillante.
/// </summary>
internal sealed class DatabaseCircuitBreakerHealthCheck(CircuitBreakerStateProvider stateProvider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = stateProvider.CircuitState;
        var result = state switch
        {
            CircuitState.Closed => HealthCheckResult.Healthy("Circuit fermé."),
            CircuitState.HalfOpen => HealthCheckResult.Degraded("Circuit semi-ouvert : test de reprise en cours."),
            _ => HealthCheckResult.Degraded($"Circuit {state} : les appels à la base sont rejetés."),
        };
        return Task.FromResult(result);
    }
}
