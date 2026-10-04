using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace CleanArchitectureSkeleton.Infrastructure.Resilience;

/// <summary>
/// Pipeline Polly v8 appliqué aux accès base de données. Les stratégies s'exécutent de l'extérieur vers l'intérieur :
///
///   appel ──► [Retry] ──► [Circuit breaker] ──► [Timeout par tentative] ──► base de données
///
///  • Timeout   : une tentative qui traîne est annulée au lieu de bloquer un thread indéfiniment.
///  • Breaker   : si la base est durablement malade, on "ouvre le circuit" et on rejette immédiatement les appels
///                (fail fast) au lieu d'empiler des requêtes vouées à l'échec → on protège la base ET l'appelant,
///                ce qui laisse à la base le temps de récupérer. Après un délai, une requête test (half-open) décide de la refermer.
///  • Retry     : absorbe les pannes brèves (SQLITE_BUSY...) grâce à un backoff exponentiel + jitter
///                (le jitter évite que toutes les instances réessaient au même instant : "thundering herd").
///
/// Le Retry est à l'EXTÉRIEUR du breaker : chaque tentative échouée est comptabilisée par le breaker.
/// Quand le circuit est ouvert, <see cref="BrokenCircuitException"/> n'est PAS réessayée (inutile) : elle remonte
/// jusqu'à l'API qui répond 503 + Retry-After.
/// </summary>
public static class DatabaseResiliencePipeline
{
    public const string Name = "database";

    public static void Configure(
        ResiliencePipelineBuilder builder,
        DatabaseResilienceOptions options,
        CircuitBreakerStateProvider stateProvider)
    {
        // Polly refuse MaxRetryAttempts = 0 : RetryCount = 0 signifie donc "pas de stratégie de retry".
        if (options.RetryCount > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.RetryCount,
                Delay = options.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<TimeoutRejectedException>()
                    .Handle<Exception>(TransientErrorDetector.IsTransient),
            });
        }

        builder
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = options.BreakerFailureRatio,
                MinimumThroughput = options.BreakerMinimumThroughput,
                SamplingDuration = options.BreakerSamplingDuration,
                BreakDuration = options.BreakerBreakDuration,
                // Un timeout compte aussi comme un échec pour le breaker.
                ShouldHandle = new PredicateBuilder()
                    .Handle<TimeoutRejectedException>()
                    .Handle<Exception>(TransientErrorDetector.IsTransient),
                // Permet à un health check de connaître l'état du circuit (Closed / Open / HalfOpen).
                StateProvider = stateProvider,
            })
            .AddTimeout(options.AttemptTimeout);
    }
}
