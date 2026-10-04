using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitectureSkeleton.Api.Tests;

/// <summary>
/// Démarre l'API COMPLÈTE en mémoire (vrai pipeline HTTP, vraie injection de dépendances, vraie base SQLite temporaire).
/// Chaque factory a sa propre base : les tests sont isolés et peuvent tourner en parallèle.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"orders-api-test-{Guid.NewGuid():N}.db");
    private readonly Dictionary<string, string?> _settings;
    private readonly Action<IServiceCollection>? _configureServices;

    public ApiFactory(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? configureServices = null)
    {
        _settings = settings ?? [];
        _configureServices = configureServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:orders-db", $"Data Source={_databasePath};Default Timeout=30");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        if (_configureServices is not null)
        {
            builder.ConfigureServices(_configureServices);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
