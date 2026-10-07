namespace CleanArchitectureSkeleton.Domain.Common;

/// <summary>
/// Marqueur pour un \u00e9v\u00e9nement de domaine : \u0022quelque chose d'important s'est produit\u0022 (au pass\u00e9).
/// Un agr\u00e9gat (ex: <c>Order</c>) les accumule dans sa propre liste pendant l'ex\u00e9cution d'une m\u00e9thode m\u00e9tier ;
/// c'est la couche Infrastructure (voir <c>EfUnitOfWork</c>) qui les collecte APR\u00E8S un succ\u00e8s et les
/// transforme en lignes d'Outbox, dans la M\u00caME transaction que l'\u00e9criture m\u00e9tier.
/// Le Domain ne sait rien de Kafka, de l'Outbox ni de la s\u00e9rialisation : il ne fait que "lever" l'\u00e9v\u00e9nement.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredOnUtc { get; }
}
