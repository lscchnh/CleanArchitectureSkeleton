using CleanArchitectureSkeleton.Api.Endpoints;
using CleanArchitectureSkeleton.Api.Middleware;
using CleanArchitectureSkeleton.Api.RateLimiting;
using CleanArchitectureSkeleton.Application;
using CleanArchitectureSkeleton.Infrastructure;
using Scalar.AspNetCore;

// ═════════════════════════════════════════════════════════════════════════════════════════
//  COMPOSITION ROOT : l'unique endroit qui connaît TOUTES les couches et les assemble.
//  Règle de dépendance de la Clean Architecture (les flèches pointent vers l'intérieur) :
//
//     Api ──► Infrastructure ──► Application ──► Domain
//      └─────────────────────────────────────────►
//
//  Domain ne référence rien ; Application ne référence que Domain ;
//  Infrastructure implémente les interfaces (ports) déclarées par Application.
// ═════════════════════════════════════════════════════════════════════════════════════════
var builder = WebApplication.CreateBuilder(args);

// Aspire : OpenTelemetry (logs/traces/métriques), service discovery, health checks par défaut.
builder.AddServiceDefaults();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiRateLimiting(builder.Configuration);

// Les enums (statut de commande) sont sérialisés en texte ("Pending") plutôt qu'en nombre (0) : contrat d'API lisible et stable.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

// Prépare la base (migrations + WAL) avant d'accepter du trafic.
await app.Services.InitializeDatabaseAsync();

// L'ordre des middlewares est important : le handler d'exceptions doit être le plus externe pour tout attraper.
app.UseExceptionHandler();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // Documentation interactive : /scalar/v1
}

app.MapOrderEndpoints();
app.MapProcessedMessageEndpoints();
app.MapDefaultEndpoints(); // /health (readiness) et /alive (liveness)

app.Run();

// Rend la classe Program visible aux tests d'intégration (WebApplicationFactory<Program>).
public partial class Program;
