// ═════════════════════════════════════════════════════════════════════════════════════════
//  .NET Aspire AppHost : décrit en C# la topologie de l'application (services, dépendances, réplicas).
//  `dotnet run --project src/CleanArchitectureSkeleton.AppHost` démarre tout + le dashboard Aspire
//  (logs, traces distribuées, métriques, état des health checks).
// ═════════════════════════════════════════════════════════════════════════════════════════
var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL est OBLIGATOIRE (nécessite Docker) : il remplace le fichier SQLite partagé.
// Contrairement à un fichier, une vraie base serveur se partage nativement entre plusieurs machines/replicas
// et plusieurs processus concurrents : c'est la base "production-like" de ce template.
// Le concurrency control pessimiste change de nature en même temps (voir EfUnitOfWork / OrderRepository) :
// SQLite verrouillait tout le fichier (BEGIN IMMEDIATE), PostgreSQL ne verrouille que la ligne lue (SELECT ... FOR UPDATE).
var postgres = builder.AddPostgres("postgres")
    .WithImage("postgres")
    .WithImageTag("17")
    // Volume nommé : les données survivent aux redémarrages de l'AppHost (comme Redis/Kafka).
    .WithDataVolume("cleanarchitectureskeleton-postgres-data");

// AddDatabase crée la base logique "orders-db" DANS le serveur PostgreSQL et expose une chaîne de connexion
// nommée "orders-db" : exactement le nom attendu par DependencyInjection.DatabaseConnectionName, donc rien
// à changer côté Infrastructure pour la résolution de la configuration.
var ordersDb = postgres.AddDatabase("orders-db");

var api = builder.AddProject<Projects.CleanArchitectureSkeleton_Api>("api")
    .WithReference(ordersDb)
    .WaitFor(ordersDb)
    // Haute disponibilité : 2 instances derrière le load balancer d'Aspire. Si l'une tombe, l'autre sert le trafic.
    .WithReplicas(2)
    // Aspire interroge /health : une instance non "ready" ne reçoit pas de trafic.
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// Redis est OBLIGATOIRE (nécessite Docker) : Aspire démarre le conteneur `redis` au lancement.
// Le cache L2 est partagé entre les réplicas ⇒ une invalidation faite par l'une profite à toutes.
var redis = builder.AddRedis("redis")
    // Image Docker épinglée pour un comportement reproductible entre postes/CI.
    .WithImage("redis")
    .WithImageTag("7.4")
    // Volume nommé : le cache survit aux redémarrages de l'AppHost.
    .WithDataVolume("cleanarchitectureskeleton-redis-data");

api.WithReference(redis).WaitFor(redis);

// Kafka est OBLIGATOIRE (nécessite Docker) : transport utilisé pour SIMULER l'envoi d'un event
// à chaque écriture en base (pattern Outbox, voir EfUnitOfWork + OutboxProcessor dans Infrastructure).
// Mode KRaft (sans Zookeeper) : un seul conteneur suffit. WithKafkaUI() expose une interface web
// (port dynamique géré par Aspire) pour observer les topics/messages publiés.
var kafka = builder.AddKafka("kafka")
    .WithImage("apache/kafka")
    .WithImageTag("3.9.1")
    // Volume nommé : les topics/messages déclarés survivent aux redémarrages de l'AppHost.
    .WithDataVolume("cleanarchitectureskeleton-kafka-data")
    .WithKafkaUI();

api.WithReference(kafka).WaitFor(kafka);

// Interface Blazor pour gérer les commandes : elle appelle l'Api via le service discovery d'Aspire ("api").
builder.AddProject<Projects.CleanArchitectureSkeleton_Web>("web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
