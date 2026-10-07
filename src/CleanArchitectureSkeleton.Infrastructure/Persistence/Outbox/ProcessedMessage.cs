namespace CleanArchitectureSkeleton.Infrastructure.Persistence.Outbox;

/// <summary>
/// Trace, côté "consommateur", de chaque event déjà traité : c'est la table qui rend le
/// <c>OutboxConsumer</c> IDEMPOTENT.
///
/// Pourquoi l'idempotence est nécessaire : Kafka garantit "au moins une fois" (at-least-once),
/// pas "exactement une fois". Un message peut donc être relu (ex: le consommateur plante juste
/// après avoir traité le message mais avant de committer l'offset). Sans protection, on traiterait
/// deux fois le même event.
///
/// <see cref="Id"/> est volontairement le MÊME Guid que l'<c>OutboxMessage.Id</c> d'origine (propagé en
/// tant que MessageId Kafka) : une simple contrainte d'unicité sur la clé primaire suffit donc à
/// détecter un doublon, sans logique métier supplémentaire.
/// </summary>
public sealed class ProcessedMessage
{
    public required Guid Id { get; init; }

    /// <summary>Nom du type d'événement (ex: "OrderCreatedDomainEvent"), repris tel quel depuis l'Outbox.</summary>
    public required string Type { get; init; }

    /// <summary>Payload JSON original : conservé pour l'affichage dans l'encart Blazor.</summary>
    public required string Content { get; init; }

    public DateTimeOffset ProcessedOnUtc { get; init; }
}
