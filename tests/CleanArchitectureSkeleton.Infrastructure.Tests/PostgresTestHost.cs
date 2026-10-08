using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace CleanArchitectureSkeleton.Infrastructure.Tests;

/// <summary>
/// Mini "composition root" de test : un VRAI conteneur PostgreSQL (via Testcontainers, nécessite Docker) et le
/// VRAI câblage d'Infrastructure (AddInfrastructure). On teste donc exactement ce qui tourne en production,
/// sans mock de la base.
/// Un conteneur dédié par host permet de tester la concurrence entre plusieurs connexions sans polluer
/// les autres tests.
/// </summary>
public sealed class PostgresTestHost : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;

    private PostgresTestHost(ServiceProvider provider, PostgreSqlContainer container)
    {
        Services = provider;
        _container = container;
    }

    public ServiceProvider Services { get; }

    public static async Task<PostgresTestHost> CreateAsync(Dictionary<string, string?>? extraSettings = null)
    {
        var container = new PostgreSqlBuilder("postgres:17").Build();
        await container.StartAsync();

        var settings = new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{DependencyInjection.DatabaseConnectionName}"] = container.GetConnectionString(),
            // Résilience rapide pour que les tests ne dorment pas.
            ["Resilience:Database:RetryBaseDelay"] = "00:00:00.001",
        };
        foreach (var (key, value) in extraSettings ?? [])
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddInfrastructure(configuration);

        var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.InitializeDatabaseAsync();
        return new PostgresTestHost(provider, container);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _container.DisposeAsync();
    }
}
