// ═════════════════════════════════════════════════════════════════════════════════════════
//  .NET Aspire AppHost : décrit en C# la topologie de l'application (services, dépendances, réplicas).
//  `dotnet run --project src/CleanArchitectureSkeleton.AppHost` démarre tout + le dashboard Aspire
//  (logs, traces distribuées, métriques, état des health checks).
// ═════════════════════════════════════════════════════════════════════════════════════════
var builder = DistributedApplication.CreateBuilder(args);

// Fichier SQLite PARTAGÉ par toutes les réplicas locales (mode WAL ⇒ lectures concurrentes, écritures sérialisées).
// ⚠ SQLite est parfait pour apprendre/démarrer, mais un fichier ne se partage pas entre machines.
//    En production multi-nœuds, on remplace le provider dans Infrastructure par PostgreSQL / SQL Server
//    (une seule couche à modifier : c'est tout l'intérêt de la Clean Architecture).
var dataDirectory = Path.Combine(builder.AppHostDirectory, ".data");
Directory.CreateDirectory(dataDirectory);
var databasePath = Path.Combine(dataDirectory, "orders.db");

var api = builder.AddProject<Projects.CleanArchitectureSkeleton_Api>("api")
    .WithEnvironment("ConnectionStrings__orders-db", $"Data Source={databasePath};Default Timeout=30")
    // Haute disponibilité : 2 instances derrière le load balancer d'Aspire. Si l'une tombe, l'autre sert le trafic.
    .WithReplicas(2)
    // Aspire interroge /health : une instance non "ready" ne reçoit pas de trafic.
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// Redis est OPTIONNEL (nécessite Docker) : `dotnet run ... --UseRedis=true`.
// Avec Redis, le cache L2 est partagé entre les réplicas ⇒ une invalidation faite par l'une profite à toutes.
// Sans Redis, chaque instance garde son cache mémoire local (durée de vie L1 courte pour limiter l'incohérence).
if (bool.TryParse(builder.Configuration["UseRedis"], out var useRedis) && useRedis)
{
    var redis = builder.AddRedis("redis");
    api.WithReference(redis).WaitFor(redis);
}

builder.Build().Run();
