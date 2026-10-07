namespace CleanArchitectureSkeleton.Infrastructure.Persistence.Outbox;

/// <summary>
/// Table d'Outbox (pattern "Transactional Outbox") : chaque \u00e9v\u00e9nement de domaine lev\u00e9 par un agr\u00e9gat
/// (<c>Order.DomainEvents</c>) est ins\u00e9r\u00e9 ICI, dans la M\u00caME transaction PostgreSQL que l'\u00e9criture m\u00e9tier
/// (voir <c>EfUnitOfWork</c>). Ainsi, soit les deux sont \u00e9crits ensemble, soit aucun ne l'est :
/// impossible d'\u00e9crire une commande sans produire son \u00e9v\u00e9nement (et inversement).
/// Un processus s\u00e9par\u00e9 (<c>OutboxProcessor</c>, un <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>)
/// lit p\u00e9riodiquement les lignes non trait\u00e9es et les publie sur Kafka, puis marque <see cref="ProcessedOnUtc"/>.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    /// <summary>Nom court du type d'\u00e9v\u00e9nement (ex: "OrderCreatedDomainEvent") : sert de clÃ© Kafka.</summary>
    public required string Type { get; init; }

    /// <summary>Payload JSON de l'\u00e9v\u00e9nement, s\u00e9rialis\u00e9 avec son type concret (System.Text.Json).</summary>
    public required string Content { get; init; }

    public DateTimeOffset OccurredOnUtc { get; init; }

    /// <summary>Null tant que l'event n'a pas \u00e9t\u00e9 publi\u00e9 avec succ\u00e8s sur Kafka.</summary>
    public DateTimeOffset? ProcessedOnUtc { get; set; }

    /// <summary>Dernier message d'erreur rencontr\u00e9 (utile pour diagnostiquer sans DLQ d\u00e9di\u00e9e).</summary>
    public string? Error { get; set; }
}
