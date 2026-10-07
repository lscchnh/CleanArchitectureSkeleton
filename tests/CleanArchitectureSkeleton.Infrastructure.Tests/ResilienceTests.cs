using CleanArchitectureSkeleton.Infrastructure.HealthChecks;
using CleanArchitectureSkeleton.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Polly;
using Polly.CircuitBreaker;

namespace CleanArchitectureSkeleton.Infrastructure.Tests;

/// <summary>
/// Tests du pipeline Polly (retry + circuit breaker). On simule des pannes en levant de vraies PostgresException
/// avec des codes SQLSTATE (40001 = serialization_failure transitoire, 23505 = violation de contrainte permanente).
/// </summary>
public class ResilienceTests
{
    private const string Busy = "40001"; // serialization_failure
    private const string Constraint = "23505"; // unique_violation

    private static readonly DatabaseResilienceOptions FastOptions = new()
    {
        RetryCount = 3,
        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
        BreakerMinimumThroughput = 4,
        BreakerFailureRatio = 0.5,
        BreakerSamplingDuration = TimeSpan.FromSeconds(30),
        BreakerBreakDuration = TimeSpan.FromMilliseconds(500),
    };

    private static (ResiliencePipeline Pipeline, CircuitBreakerStateProvider State) Build(DatabaseResilienceOptions? options = null)
    {
        var state = new CircuitBreakerStateProvider();
        var builder = new ResiliencePipelineBuilder();
        DatabaseResiliencePipeline.Configure(builder, options ?? FastOptions, state);
        return (builder.Build(), state);
    }

    private static PostgresException Failure(string sqlState) =>
        new(messageText: "simulated", severity: "ERROR", invariantSeverity: "ERROR", sqlState: sqlState);

    // ── TransientErrorDetector ──────────────────────────────────────────────────

    [Theory]
    [InlineData(Busy, true)]
    [InlineData("40P01", true)] // deadlock_detected
    [InlineData("08006", true)] // connection_failure
    [InlineData(Constraint, false)]
    [InlineData("42601", false)] // erreur SQL générique : permanente
    public void Detector_classifies_postgres_error_codes(string code, bool expected)
    {
        Assert.Equal(expected, TransientErrorDetector.IsTransient(Failure(code)));
    }

    [Fact]
    public void Detector_looks_through_wrapping_exceptions()
    {
        var wrapped = new DbUpdateException("EF wrapper", Failure(Busy));

        Assert.True(TransientErrorDetector.IsTransient(wrapped));
        Assert.False(TransientErrorDetector.IsTransient(new InvalidOperationException()));
        Assert.False(TransientErrorDetector.IsTransient(null));
    }

    // ── Retry ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retry_recovers_from_transient_failures()
    {
        var (pipeline, _) = Build();
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts < 3 ? throw Failure(Busy) : ValueTask.FromResult("ok");
        });

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts); // 2 échecs + 1 succès
    }

    [Fact]
    public async Task Retry_gives_up_after_the_configured_number_of_attempts()
    {
        var (pipeline, _) = Build();
        var attempts = 0;

        await Assert.ThrowsAsync<PostgresException>(async () => await pipeline.ExecuteAsync<string>(_ =>
        {
            attempts++;
            throw Failure(Busy);
        }));

        Assert.Equal(1 + FastOptions.RetryCount, attempts);
    }

    [Fact]
    public async Task Permanent_errors_are_not_retried()
    {
        var (pipeline, _) = Build();
        var attempts = 0;

        await Assert.ThrowsAsync<PostgresException>(async () => await pipeline.ExecuteAsync<string>(_ =>
        {
            attempts++;
            throw Failure(Constraint);
        }));

        Assert.Equal(1, attempts);
    }

    // ── Circuit breaker ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Circuit_opens_after_repeated_failures_then_fails_fast()
    {
        var (pipeline, state) = Build(new DatabaseResilienceOptions
        {
            RetryCount = 0, // on isole le breaker : une tentative = un appel
            BreakerMinimumThroughput = 4,
            BreakerFailureRatio = 0.5,
            BreakerBreakDuration = TimeSpan.FromSeconds(30),
        });
        var calls = 0;

        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<PostgresException>(async () => await pipeline.ExecuteAsync<string>(_ =>
            {
                calls++;
                throw Failure(Busy);
            }));
        }

        Assert.Equal(CircuitState.Open, state.CircuitState);

        // Circuit ouvert : l'opération n'est MÊME PAS exécutée (fail fast), on reçoit directement BrokenCircuitException.
        await Assert.ThrowsAsync<BrokenCircuitException>(async () => await pipeline.ExecuteAsync(_ =>
        {
            calls++;
            return ValueTask.FromResult("never");
        }));
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task Circuit_recovers_through_half_open_after_the_break_duration()
    {
        var (pipeline, state) = Build(new DatabaseResilienceOptions
        {
            RetryCount = 0,
            BreakerMinimumThroughput = 2,
            BreakerFailureRatio = 0.5,
            BreakerBreakDuration = TimeSpan.FromMilliseconds(500), // Polly minimum
        });

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<PostgresException>(async () =>
                await pipeline.ExecuteAsync<string>(_ => throw Failure(Busy)));
        }

        Assert.Equal(CircuitState.Open, state.CircuitState);

        await Task.Delay(900);
        var result = await pipeline.ExecuteAsync(_ => ValueTask.FromResult("back"));

        Assert.Equal("back", result);
        Assert.Equal(CircuitState.Closed, state.CircuitState);
    }

    [Fact]
    public async Task Business_or_permanent_errors_do_not_open_the_circuit()
    {
        var (pipeline, state) = Build(new DatabaseResilienceOptions { RetryCount = 0, BreakerMinimumThroughput = 2 });

        for (var i = 0; i < 10; i++)
        {
            await Assert.ThrowsAsync<PostgresException>(async () =>
                await pipeline.ExecuteAsync<string>(_ => throw Failure(Constraint)));
        }

        Assert.Equal(CircuitState.Closed, state.CircuitState);
    }

    // ── Health check du breaker ─────────────────────────────────────────────────

    [Fact]
    public async Task Health_check_reports_healthy_when_closed_and_degraded_when_open()
    {
        var (pipeline, state) = Build(new DatabaseResilienceOptions
        {
            RetryCount = 0,
            BreakerMinimumThroughput = 2,
            BreakerBreakDuration = TimeSpan.FromSeconds(30),
        });
        var check = new DatabaseCircuitBreakerHealthCheck(state);

        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<PostgresException>(async () =>
                await pipeline.ExecuteAsync<string>(_ => throw Failure(Busy)));
        }

        Assert.Equal(HealthStatus.Degraded, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
    }
}
