# CleanArchitectureSkeleton

Template **.NET 10 + .NET Aspire** en **Clean Architecture**, à but **éducatif** : chaque choix est expliqué dans les commentaires du code.
Cas d'usage fil rouge : un **CRUD de commandes (Orders)**, volontairement « overkill » pour montrer toutes les briques d'un service industrialisable.

> Architecture en **services classiques** (pas de CQRS / MediatR) : une interface `IOrderService`, une classe `OrderService`.

## Démarrage rapide

```bash
dotnet run --project src/CleanArchitectureSkeleton.AppHost      # Aspire : 2 réplicas de l'API + dashboard
dotnet run --project src/CleanArchitectureSkeleton.Api          # ou l'API seule (http://localhost:5170)
dotnet test                                                      # 100+ tests, aucune dépendance externe
```

* Documentation interactive de l'API : `/scalar/v1` (en Development) — fichier `.http` fourni dans `src/CleanArchitectureSkeleton.Api`.
* Redis optionnel (nécessite Docker) comme cache L2 partagé entre réplicas : `dotnet run --project src/CleanArchitectureSkeleton.AppHost -- --UseRedis=true`.
* Prérequis : SDK .NET 10. L'orchestration Aspire (AppHost) utilise le dashboard Aspire ; Docker n'est requis que pour Redis.

## Les couches et la règle de dépendance

```
 Api ──► Infrastructure ──► Application ──► Domain          (les flèches pointent vers l'intérieur)
```

| Projet | Rôle | Dépend de |
|---|---|---|
| `Domain` | Entités riches (`Order`, `OrderLine`), règles métier, machine à états, pattern **Result** (`Result`, `Error`) | rien |
| `Application` | Cas d'usage (`OrderService`), DTOs, **ports** (`IOrderRepository`, `IUnitOfWork`, `ICacheService`) | Domain |
| `Infrastructure` | Adaptateurs : EF Core + SQLite, transaction + verrou pessimiste, HybridCache, pipeline Polly, health checks | Application |
| `Api` | Minimal API, rate limiting, gestion d'exceptions, mapping `Result` → HTTP, composition root | tout (pour câbler) |
| `AppHost` / `ServiceDefaults` | Orchestration Aspire, OpenTelemetry, health checks par défaut | — |

Cette règle est **vérifiée automatiquement** par `CleanArchitectureSkeleton.Architecture.Tests` (NetArchTest).

## Où trouver chaque exigence

| Concept | Où | Idée clé |
|---|---|---|
| **Pattern Result** | `Domain/Common/Result.cs`, `Error.cs`, `Api/Extensions/ResultExtensions.cs` | Les erreurs métier sont des valeurs, pas des exceptions. `ErrorType` → code HTTP uniquement dans l'API. |
| **Minimal API** | `Api/Endpoints/OrderEndpoints.cs` | Endpoints fins : HTTP → service → `Result` → HTTP. |
| **SQLite + EF Core** | `Infrastructure/Persistence/*` (+ `Migrations/`) | Mapping en `IEntityTypeConfiguration`, `Order` reste pur. |
| **Transaction** | `Infrastructure/Persistence/EfUnitOfWork.cs` | Tout le travail est atomique : commit si `Result` OK, rollback sinon/exception. |
| **Concurrency pessimiste** | `EfUnitOfWork` + `OrderRepository.GetByIdForUpdateAsync` | `BEGIN IMMEDIATE` (équivalent SQLite d'un `SELECT … FOR UPDATE`) : on verrouille *avant* de lire. Prouvé par un test à 12 écrivains concurrents. |
| **Cache** | `Infrastructure/Caching/HybridCacheService.cs`, `OrderService.GetByIdAsync` | Cache-aside, L1 mémoire + L2 Redis optionnel, anti-stampede, invalidation après commit. |
| **Retry (Polly)** | `Infrastructure/Resilience/DatabaseResiliencePipeline.cs` | Backoff exponentiel + jitter, uniquement sur erreurs *transitoires*, rejoue la transaction entière. |
| **Circuit breaker** | idem + `GlobalExceptionHandler` | Ouvert ⇒ fail fast ⇒ `503` + `Retry-After`. État exposé en health check. |
| **Rate limiting (token bucket)** | `Api/RateLimiting/RateLimitingExtensions.cs` | Un seau par IP, rafales tolérées, `429` + `Retry-After`. Configurable. |
| **Health checks** | `ServiceDefaults/Extensions.cs`, `Infrastructure/HealthChecks` | `/alive` (liveness, ne vérifie rien d'externe) et `/health` (readiness : base + état du breaker). |
| **Observabilité** | `ServiceDefaults` | OpenTelemetry (logs, traces, métriques) vers le dashboard Aspire. |

## Tests « à tous les niveaux »

| Projet | Niveau | Ce qu'il prouve |
|---|---|---|
| `Domain.Tests` | Unitaire pur | invariants, machine à états, `Result` |
| `Application.Tests` | Unitaire avec fakes en mémoire | orchestration : transaction, verrou, cache-aside + invalidation, mapping d'erreurs |
| `Infrastructure.Tests` | Intégration (vraie SQLite) | repository, commit/rollback, **verrou pessimiste**, retry, circuit breaker, cache, health checks |
| `Api.Tests` | Bout en bout (`WebApplicationFactory`) | CRUD HTTP, codes de statut, cache non périmé, concurrence, rate limiting, 503/500 |
| `Architecture.Tests` | Architecture | règle de dépendance, encapsulation, ports |

## Haute disponibilité et passage à l'échelle : ce qui est prêt… et ce qu'il reste à faire

**Déjà en place**
* API **sans état** (le cache L2 et la base sont externes) ⇒ scalable horizontalement ; 2 réplicas dans l'AppHost.
* Probes liveness/readiness, arrêt gracieux (hôte ASP.NET), timeouts, retry, circuit breaker, rate limiting, ProblemDetails (RFC 9457).
* Central Package Management, warnings = erreurs, CI GitHub Actions (`.github/workflows/ci.yml`), Dockerfile (non testé ici).

**À faire pour une vraie production multi-nœuds** (volontairement hors périmètre d'un template SQLite)
1. **Remplacer SQLite** par PostgreSQL/SQL Server : un fichier ne se partage pas entre machines. Seule la couche `Infrastructure` change ; sur ces bases, `GetByIdForUpdateAsync` devient un vrai verrou de ligne (`FOR UPDATE` / `UPDLOCK`).
2. **Rate limiting global** : les compteurs sont par instance ⇒ à déléguer à la passerelle/ingress ou à Redis.
3. **Migrations** via un *migration bundle* dans le pipeline de déploiement plutôt qu'au démarrage.
4. **Authentification/autorisation**, **idempotence** des POST (`Idempotency-Key`), **versioning** d'API.
5. Réplication/sauvegardes de la base, HTTPS terminé au niveau de l'ingress.

## Structure

```
src/
  CleanArchitectureSkeleton.Domain/          # cœur métier, zéro dépendance
  CleanArchitectureSkeleton.Application/     # cas d'usage + ports
  CleanArchitectureSkeleton.Infrastructure/  # EF Core, SQLite, cache, Polly, health checks
  CleanArchitectureSkeleton.Api/             # Minimal API, composition root
  CleanArchitectureSkeleton.ServiceDefaults/ # Aspire : télémétrie, health checks
  CleanArchitectureSkeleton.AppHost/         # Aspire : orchestration
tests/                                       # un projet de tests par niveau
```

## Utiliser ce template

Cloner, renommer les projets/namespaces, puis remplacer le sous-domaine `Orders` par le vôtre en conservant le patron : entité riche → erreurs → port → service → endpoints → tests.
