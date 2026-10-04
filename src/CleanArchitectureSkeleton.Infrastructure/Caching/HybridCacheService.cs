using CleanArchitectureSkeleton.Application.Abstractions;
using Microsoft.Extensions.Caching.Hybrid;

namespace CleanArchitectureSkeleton.Infrastructure.Caching;

/// <summary>
/// Adaptateur du port <see cref="ICacheService"/> basé sur <see cref="HybridCache"/> (.NET 9+) :
///  • L1 = mémoire locale du processus (ultra rapide) ;
///  • L2 = cache distribué (Redis) partagé entre les instances, si configuré → cohérence en scale-out ;
///  • protection native contre le cache stampede : 1000 requêtes simultanées sur une clé absente = UN seul accès base.
/// </summary>
internal sealed class HybridCacheService(HybridCache cache) : ICacheService
{
    // HybridCache mémorise aussi les valeurs null ; ce conteneur nous permet de détecter "pas trouvé" et de ne pas le conserver.
    private sealed record Entry<T>(T? Value);

    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var ttl = expiration ?? TimeSpan.FromMinutes(5);
        var options = new HybridCacheEntryOptions
        {
            Expiration = ttl,
            // L1 plus courte que L2 : une instance rattrape plus vite une invalidation faite par une autre.
            LocalCacheExpiration = TimeSpan.FromSeconds(Math.Min(30, ttl.TotalSeconds)),
        };

        var entry = await cache.GetOrCreateAsync(
            key,
            async ct => new Entry<T>(await factory(ct)),
            options,
            cancellationToken: cancellationToken);

        if (entry.Value is null)
        {
            // Ne pas conserver les "introuvable" : la donnée peut apparaître juste après.
            await cache.RemoveAsync(key, cancellationToken);
        }

        return entry.Value;
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        await cache.RemoveAsync(key, cancellationToken);
}
