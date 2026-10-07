using CleanArchitectureSkeleton.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CleanArchitectureSkeleton.Infrastructure.Tests;

public class CacheAndHealthTests : IAsyncLifetime
{
    private PostgresTestHost _host = null!;

    public async Task InitializeAsync() => _host = await PostgresTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private sealed record Payload(string Name);

    [Fact]
    public async Task Cache_executes_the_factory_once_per_key()
    {
        var cache = _host.Services.GetRequiredService<ICacheService>();
        var calls = 0;
        Task<Payload?> Factory(CancellationToken _) { calls++; return Task.FromResult<Payload?>(new Payload("v")); }

        var first = await cache.GetOrCreateAsync("k1", Factory);
        var second = await cache.GetOrCreateAsync("k1", Factory);

        Assert.Equal(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cache_remove_forces_a_refresh()
    {
        var cache = _host.Services.GetRequiredService<ICacheService>();
        var version = 0;
        Task<Payload?> Factory(CancellationToken _) => Task.FromResult<Payload?>(new Payload($"v{++version}"));

        Assert.Equal("v1", (await cache.GetOrCreateAsync("k2", Factory))!.Name);
        await cache.RemoveAsync("k2");

        Assert.Equal("v2", (await cache.GetOrCreateAsync("k2", Factory))!.Name);
    }

    [Fact]
    public async Task Cache_does_not_keep_null_results()
    {
        var cache = _host.Services.GetRequiredService<ICacheService>();
        var calls = 0;
        Task<Payload?> Missing(CancellationToken _) { calls++; return Task.FromResult<Payload?>(null); }

        Assert.Null(await cache.GetOrCreateAsync("k3", Missing));
        Assert.Null(await cache.GetOrCreateAsync("k3", Missing));

        Assert.Equal(2, calls); // le "introuvable" n'est pas mémorisé
    }

    [Fact]
    public async Task Cache_protects_against_stampede_on_concurrent_requests()
    {
        var cache = _host.Services.GetRequiredService<ICacheService>();
        var calls = 0;

        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => cache.GetOrCreateAsync("k4", async ct =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(100, ct);
            return new Payload("slow");
        })));

        Assert.All(results, r => Assert.Equal("slow", r!.Name));
        Assert.Equal(1, calls); // 25 demandes simultanées ⇒ 1 seul accès à la source
    }

    [Fact]
    public async Task Readiness_health_checks_are_healthy_on_a_working_database()
    {
        var service = _host.Services.GetRequiredService<HealthCheckService>();

        var report = await service.CheckHealthAsync(r => r.Tags.Contains("ready"));

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Contains("database", report.Entries.Keys);
        Assert.Contains("database-circuit-breaker", report.Entries.Keys);
    }
}
