using CleanArchitectureSkeleton.Application.Abstractions;
using CleanArchitectureSkeleton.Infrastructure.Caching;
using CleanArchitectureSkeleton.Infrastructure.HealthChecks;
using CleanArchitectureSkeleton.Infrastructure.Persistence;
using CleanArchitectureSkeleton.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;

namespace CleanArchitectureSkeleton.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "orders-db";
    public const string RedisConnectionName = "redis";

    /// <summary>Enregistre tout ce qui touche au monde extérieur : base, cache, résilience, health checks.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ── Persistance ──────────────────────────────────────────────────────────────
        var connectionString = configuration.GetConnectionString(DatabaseConnectionName)
            ?? "Data Source=orders.db;Default Timeout=30";

        // AddDbContext = scope "par requête" : un DbContext par requête HTTP (il n'est PAS thread-safe).
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // ── Résilience (Polly) ───────────────────────────────────────────────────────
        services.Configure<DatabaseResilienceOptions>(configuration.GetSection(DatabaseResilienceOptions.SectionName));

        // Le StateProvider est un singleton partagé : le breaker y écrit son état, le health check le lit.
        var stateProvider = new CircuitBreakerStateProvider();
        services.AddSingleton(stateProvider);

        services.AddResiliencePipeline(DatabaseResiliencePipeline.Name, (builder, context) =>
        {
            var options = context.ServiceProvider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseResilienceOptions>>().Value;
            DatabaseResiliencePipeline.Configure(builder, options, stateProvider);
        });

        // ── Cache ────────────────────────────────────────────────────────────────────
        // Si une chaîne de connexion Redis est fournie (ex: par Aspire), elle devient le cache L2 partagé
        // entre toutes les instances. Sinon on reste en cache mémoire local : l'appli fonctionne partout.
        var redis = configuration.GetConnectionString(RedisConnectionName);
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(o => o.Configuration = redis);
        }

        services.AddHybridCache();
        services.AddSingleton<ICacheService, HybridCacheService>();

        // ── Health checks ────────────────────────────────────────────────────────────
        // Tag "ready"  : l'instance peut-elle servir du trafic ? (base joignable) → sonde de readiness.
        // Le tag "live" (défini côté API) ne vérifie rien d'externe : "le processus répond-il ?"
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
            .AddCheck<DatabaseCircuitBreakerHealthCheck>("database-circuit-breaker", tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Prépare la base au démarrage : applique les migrations et active le mode WAL
    /// (Write-Ahead Logging : lecteurs et écrivain ne se bloquent plus mutuellement, bien meilleur en concurrence).
    /// En production sérieuse, on préférerait un "migration bundle" exécuté par le pipeline de déploiement
    /// plutôt qu'au démarrage de chaque instance.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        // Plusieurs instances peuvent démarrer en même temps sur le même fichier : on retente quelques fois
        // si l'une d'elles est en train de migrer.
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync(cancellationToken);
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
                return;
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }
    }
}
