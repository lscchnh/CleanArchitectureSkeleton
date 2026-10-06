using System.Threading.RateLimiting;

namespace CleanArchitectureSkeleton.Api.RateLimiting;

/// <summary>Paramètres du token bucket, modifiables par configuration (section "RateLimiting").</summary>
public sealed class TokenBucketRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Capacité du seau = taille maximale d'une rafale autorisée.</summary>
    public int TokenLimit { get; set; } = 50;

    /// <summary>Jetons ajoutés à chaque période = débit moyen soutenu autorisé.</summary>
    public int TokensPerPeriod { get; set; } = 10;

    public TimeSpan ReplenishmentPeriod { get; set; } = TimeSpan.FromSeconds(1);
}

public static class RateLimitingExtensions
{
    public const string OrdersPolicy = "orders";

    /// <summary>
    /// RATE LIMITING par algorithme TOKEN BUCKET :
    /// chaque client dispose d'un seau de N jetons ; chaque requête en consomme un ; le seau se remplit à débit constant.
    ///  → les rafales sont tolérées (tant que le seau n'est pas vide),
    ///  → mais le débit moyen reste plafonné (≈ TokensPerPeriod / ReplenishmentPeriod).
    /// Seau vide → 429 Too Many Requests + en-tête Retry-After. On protège ainsi la base contre les abus
    /// et les clients bavards (« noisy neighbors »).
    ///
    /// ⚠ Les compteurs sont en mémoire de CHAQUE instance : avec N instances, la limite effective globale est N×.
    /// Pour une limite exacte à l'échelle du cluster, appliquer le rate limiting au niveau de la passerelle (API Gateway, YARP, ingress)
    /// ou utiliser un compteur partagé (Redis).
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(TokenBucketRateLimitOptions.SectionName).Get<TokenBucketRateLimitOptions>()
            ?? new TokenBucketRateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy(OrdersPolicy, httpContext =>
                // Une partition (= un seau) PAR client, identifié ici par son IP.
                // Derrière un proxy, activer UseForwardedHeaders pour obtenir la vraie IP du client.
                RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = options.TokenLimit,
                        TokensPerPeriod = options.TokensPerPeriod,
                        ReplenishmentPeriod = options.ReplenishmentPeriod,
                        AutoReplenishment = true,
                        QueueLimit = 0, // pas de file d'attente : on rejette tout de suite (fail fast).
                    }));

            // OnRejected = callback pour personnaliser la réponse HTTP 429 Too Many Requests.
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new Microsoft.AspNetCore.Mvc.ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "RateLimit.Exceeded",
                        Detail = "Trop de requêtes. Réessayez après le délai indiqué dans Retry-After.",
                    },
                    cancellationToken);
            };
        });

        return services;
    }
}
