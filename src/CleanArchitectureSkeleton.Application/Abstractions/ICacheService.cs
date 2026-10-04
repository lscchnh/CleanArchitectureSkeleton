namespace CleanArchitectureSkeleton.Application.Abstractions;

/// <summary>
/// PORT de cache. L'Application exprime un besoin ("mets ça en cache"), Infrastructure choisit la techno
/// (mémoire locale, Redis, ou les deux via HybridCache).
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Pattern cache-aside : renvoie la valeur en cache, sinon exécute <paramref name="factory"/> et mémorise le résultat.
    /// Un résultat <c>null</c> n'est PAS mémorisé (on ne met pas en cache les "introuvable").
    /// Les appels concurrents pour une même clé n'exécutent la factory qu'une fois (anti cache stampede).
    /// </summary>
    Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>Invalide une entrée (à appeler après toute modification de la donnée source).</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
