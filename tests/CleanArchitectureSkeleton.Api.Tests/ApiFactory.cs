using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace CleanArchitectureSkeleton.Api.Tests;

/// <summary>
/// Démarre l'API COMPLÈTE en mémoire (vrai pipeline HTTP, vraie injection de dépendances, vrai conteneur
/// PostgreSQL via Testcontainers — nécessite Docker). Chaque factory démarre son propre conteneur :
/// les tests sont isolés et peuvent tourner en parallèle.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // WebApplicationFactory n'offre pas de hook async pour la construction : on démarre le conteneur
    // de façon synchrone (bloquante) dans le constructeur, ce qui reste acceptable pour des tests.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:17").Build();
    private readonly Dictionary<string, string?> _settings;
    private readonly Action<IServiceCollection>? _configureServices;

    public ApiFactory(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? configureServices = null)
    {
        _settings = settings ?? [];
        _configureServices = configureServices;
        _container.StartAsync().GetAwaiter().GetResult();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:orders-db", _container.GetConnectionString());
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

        _container.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
