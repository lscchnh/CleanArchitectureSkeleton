using System.Net;
using CleanArchitectureSkeleton.Application.Orders;
using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Polly.CircuitBreaker;

namespace CleanArchitectureSkeleton.Api.Tests;

/// <summary>Préoccupations transverses : health checks, rate limiting, gestion des pannes (circuit breaker → 503).</summary>
public class PlatformTests
{
    [Fact]
    public async Task Health_endpoints_report_healthy()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var ready = await client.GetAsync("/health");
        var live = await client.GetAsync("/alive");

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task Rate_limiter_token_bucket_rejects_requests_once_the_bucket_is_empty()
    {
        // Seau de 3 jetons, réapprovisionné de 1 jeton par minute ⇒ pendant le test, seules 3 requêtes passent.
        using var factory = new ApiFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:TokenLimit"] = "3",
            ["RateLimiting:TokensPerPeriod"] = "1",
            ["RateLimiting:ReplenishmentPeriod"] = "00:01:00",
        });
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 5; i++)
        {
            var response = await client.GetAsync("/api/orders");
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected ??= response;
            }
        }

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
        Assert.True(rejected!.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Rate_limiter_does_not_throttle_health_endpoints()
    {
        using var factory = new ApiFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:TokenLimit"] = "1",
            ["RateLimiting:TokensPerPeriod"] = "1",
            ["RateLimiting:ReplenishmentPeriod"] = "00:01:00",
        });
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/alive")).StatusCode);
        }
    }

    [Fact]
    public async Task Open_circuit_breaker_is_translated_to_503_with_retry_after()
    {
        using var factory = new ApiFactory(configureServices: services =>
        {
            services.RemoveAll<IOrderService>();
            services.AddScoped<IOrderService, BrokenOrderService>();
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.Contains("Retry-After"));
        Assert.Contains("Database.Unavailable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unexpected_exception_returns_a_500_without_leaking_details()
    {
        using var factory = new ApiFactory(configureServices: services =>
        {
            services.RemoveAll<IOrderService>();
            services.AddScoped<IOrderService, ExplodingOrderService>();
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("secret internal detail", body);
        Assert.Contains("Server.Error", body);
    }

    // Faux services qui simulent une panne : pas besoin de casser vraiment la base pour tester la réaction de l'API.
    private class FailingOrderService(Func<Exception> exception) : IOrderService
    {
        private Task<T> Fail<T>() => Task.FromException<T>(exception());

        public Task<Result<OrderDto>> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default) => Fail<Result<OrderDto>>();
        public Task<Result<OrderDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Fail<Result<OrderDto>>();
        public Task<Result<PagedResult<OrderDto>>> ListAsync(int page, int pageSize, OrderStatus? status, CancellationToken cancellationToken = default) => Fail<Result<PagedResult<OrderDto>>>();
        public Task<Result<OrderDto>> UpdateAsync(Guid id, UpdateOrderRequest request, CancellationToken cancellationToken = default) => Fail<Result<OrderDto>>();
        public Task<Result<OrderDto>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default) => Fail<Result<OrderDto>>();
        public Task<Result<OrderDto>> ShipAsync(Guid id, CancellationToken cancellationToken = default) => Fail<Result<OrderDto>>();
        public Task<Result<OrderDto>> CancelAsync(Guid id, CancellationToken cancellationToken = default) => Fail<Result<OrderDto>>();
        public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Fail<Result>();
    }

    private sealed class BrokenOrderService() : FailingOrderService(() => new BrokenCircuitException("circuit open"));

    private sealed class ExplodingOrderService() : FailingOrderService(() => new InvalidOperationException("secret internal detail"));
}
