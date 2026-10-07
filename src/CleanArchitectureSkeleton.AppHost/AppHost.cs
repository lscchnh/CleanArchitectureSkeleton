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

// Redis est OBLIGATOIRE (nécessite Docker) : Aspire démarre le conteneur `redis` au lancement.
// Le cache L2 est partagé entre les réplicas ⇒ une invalidation faite par l'une profite à toutes.
var redis = builder.AddRedis("redis")
    // Image Docker épinglée pour un comportement reproductible entre postes/CI.
    .WithImage("redis")
    .WithImageTag("7.4")
    // Volume nommé : le cache survit aux redémarrages de l'AppHost.
    .WithDataVolume("cleanarchitectureskeleton-redis-data");

api.WithReference(redis).WaitFor(redis);

// Interface Blazor pour gérer les commandes : elle appelle l'Api via le service discovery d'Aspire ("api").
builder.AddProject<Projects.CleanArchitectureSkeleton_Web>("web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
