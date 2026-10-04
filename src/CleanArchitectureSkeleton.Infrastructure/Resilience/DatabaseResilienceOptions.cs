namespace CleanArchitectureSkeleton.Infrastructure.Resilience;

/// <summary>
/// Paramètres de résilience, modifiables par configuration (section "Resilience:Database" de appsettings.json)
/// sans recompiler : on peut les ajuster en production selon le comportement observé.
/// </summary>
public sealed class DatabaseResilienceOptions
{
    public const string SectionName = "Resilience:Database";

    /// <summary>Nombre de nouvelles tentatives après le premier échec transitoire.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Délai de base du backoff exponentiel (avec jitter) entre deux tentatives.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Durée maximale d'UNE tentative.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Fenêtre d'observation du circuit breaker.</summary>
    public TimeSpan BreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Proportion d'échecs (0..1) dans la fenêtre qui fait s'ouvrir le circuit.</summary>
    public double BreakerFailureRatio { get; set; } = 0.5;

    /// <summary>Nombre minimal d'appels dans la fenêtre avant d'évaluer le ratio (évite d'ouvrir sur 1 échec / 1 appel).</summary>
    public int BreakerMinimumThroughput { get; set; } = 10;

    /// <summary>Durée pendant laquelle le circuit reste ouvert (appels rejetés immédiatement) avant un essai.</summary>
    public TimeSpan BreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(15);
}
