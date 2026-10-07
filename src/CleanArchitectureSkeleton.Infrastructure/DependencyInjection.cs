using CleanArchitectureSkeleton.Application.Abstractions;
using CleanArchitectureSkeleton.Infrastructure.Caching;
using CleanArchitectureSkeleton.Infrastructure.HealthChecks;
using CleanArchitectureSkeleton.Infrastructure.Messaging;
using CleanArchitectureSkeleton.Infrastructure.Persistence;
using CleanArchitectureSkeleton.Infrastructure.Resilience;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;

namespace CleanArchitectureSkeleton.Infrastructure;

public static class DependencyInjection
{
    public const string DatabaseConnectionName = "orders-db";
    public const string RedisConnectionName = "redis";
    public const string KafkaConnectionName = "kafka";
    private const string OutboxTopicName = "orders-events";
    private const string OutboxConsumerGroupId = "orders-events-processed-messages";

    /// <summary>Enregistre tout ce qui touche au monde exterieur : base, cache, resilience, health checks.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // -- Persistance --
        // En local hors Aspire (ex: dotnet run direct sur l'Api), on retombe sur un PostgreSQL
        // lance manuellement (ex: docker run postgres:17) sur le port par defaut.
        var connectionString = configuration.GetConnectionString(DatabaseConnectionName)
            ?? "Host=localhost;Database=orders-db;Username=postgres;Password=postgres";

        // AddDbContext = scope "par requete" : un DbContext par requete HTTP (il n'est PAS thread-safe).
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // -- Resilience (Polly) --
        services.Configure<DatabaseResilienceOptions>(configuration.GetSection(DatabaseResilienceOptions.SectionName));

        // Le StateProvider est un singleton partage : le breaker y ecrit son etat, le health check le lit.
        var stateProvider = new CircuitBreakerStateProvider();
        services.AddSingleton(stateProvider);

        services.AddResiliencePipeline(DatabaseResiliencePipeline.Name, (builder, context) =>
        {
            var options = context.ServiceProvider
                .GetRequiredService<IOptions<DatabaseResilienceOptions>>().Value;
            DatabaseResiliencePipeline.Configure(builder, options, stateProvider);
        });

        // -- Cache --
        // Si une chaine de connexion Redis est fournie (ex: par Aspire), elle devient le cache L2 partage
        // entre toutes les instances. Sinon on reste en cache memoire local : l'appli fonctionne partout.
        var redis = configuration.GetConnectionString(RedisConnectionName);
        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(o => o.Configuration = redis);
        }

        services.AddHybridCache();
        services.AddSingleton<ICacheService, HybridCacheService>();

        // -- Simulation d'events (pattern Outbox + Kafka) --
        // Chaque ecriture metier (Order creee/modifiee/confirmee/expediee/annulee) depose un event dans
        // la table OutboxMessages (voir EfUnitOfWork). Kafka (conteneur Docker fourni par l'AppHost)
        // n'est QUE le transport : on simule ainsi l'envoi d'un event a chaque ecriture, sans coupler
        // directement la transaction metier au broker (si Kafka est down, les ecritures continuent ;
        // l'OutboxProcessor rattrapera la publication plus tard).
        var kafkaConnectionString = configuration.GetConnectionString(KafkaConnectionName);
        if (!string.IsNullOrWhiteSpace(kafkaConnectionString))
        {
            services.AddSingleton<IProducer<string, string>>(_ =>
                new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafkaConnectionString }).Build());
            services.AddSingleton<IConsumer<string, string>>(_ =>
                new ConsumerBuilder<string, string>(new ConsumerConfig
                {
                    BootstrapServers = kafkaConnectionString,
                    GroupId = OutboxConsumerGroupId,
                    // On lit depuis le debut du topic au premier demarrage (aucun offset committe),
                    // puis on repart toujours du dernier offset committe (voir OutboxConsumer.Commit).
                    AutoOffsetReset = AutoOffsetReset.Earliest,
                    // On committe nous-memes, explicitement, apres ecriture reussie en base
                    // (voir OutboxConsumer.HandleMessageAsync) : jamais de perte de message.
                    EnableAutoCommit = false,
                }).Build());

            // Creation du topic avec UNE SEULE partition : condition necessaire a l'ordre FIFO garanti
            // par OutboxConsumer (voir ses commentaires). Idempotent si le topic existe deja.
            using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafkaConnectionString }).Build())
            {
                try
                {
                    admin.CreateTopicsAsync([new TopicSpecification { Name = OutboxTopicName, NumPartitions = 1, ReplicationFactor = 1 }])
                        .GetAwaiter().GetResult();
                }
                catch (CreateTopicsException)
                {
                    // Le topic existe deja (ex: redemarrage de l'AppHost avec volume persistant) : rien a faire.
                }
            }

            services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
            services.AddHostedService<OutboxProcessor>();
            // Consommateur "metier" : stocke chaque event traite dans ProcessedMessages (idempotent, ordonne).
            services.AddScoped<IProcessedMessageQueries, ProcessedMessageQueries>();
            services.AddHostedService<OutboxConsumer>();
        }

        // -- Health checks --
        // Tag "ready"  : l'instance peut-elle servir du trafic ? (base joignable) -> sonde de readiness.
        // Le tag "live" (defini cote API) ne verifie rien d'externe : "le processus repond-il ?"
        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
            .AddCheck<DatabaseCircuitBreakerHealthCheck>("database-circuit-breaker", tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Prepare la base au demarrage : applique les migrations EF Core.
    /// Plusieurs instances peuvent demarrer en meme temps sur le meme serveur PostgreSQL : on retente
    /// quelques fois si l'une d'elles est en train de migrer ou si la base n'accepte pas encore de connexions
    /// (demarrage a froid du conteneur Docker lance par Aspire).
    /// En production serieuse, on preffererait un "migration bundle" execute par le pipeline de deploiement
    /// plutot qu'au demarrage de chaque instance.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync(cancellationToken);
                return;
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }
    }
}
