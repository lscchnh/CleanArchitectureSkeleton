using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitectureSkeleton.Infrastructure.Tests;

/// <summary>
/// Mini "composition root" de test : une VRAIE base SQLite (fichier temporaire) et le VRAI câblage d'Infrastructure
/// (AddInfrastructure). On teste donc exactement ce qui tourne en production, sans mock de la base.
/// Un fichier (et non ":memory:") est nécessaire pour tester la concurrence entre plusieurs connexions.
/// </summary>
public sealed class SqliteTestHost : IAsyncDisposable
{
    private readonly string _path;

    private SqliteTestHost(ServiceProvider provider, string path)
    {
        Services = provider;
        _path = path;
    }

    public ServiceProvider Services { get; }

    public static async Task<SqliteTestHost> CreateAsync(Dictionary<string, string?>? extraSettings = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"orders-test-{Guid.NewGuid():N}.db");
        var settings = new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{DependencyInjection.DatabaseConnectionName}"] = $"Data Source={path};Default Timeout=30",
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
        return new SqliteTestHost(provider, path);
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        SqliteConnection.ClearAllPools(); // libère le fichier sous Windows
        foreach (var file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
