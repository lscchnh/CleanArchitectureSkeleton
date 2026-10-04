using CleanArchitectureSkeleton.Domain.Common;

namespace CleanArchitectureSkeleton.Domain.Orders;

/// <summary>
/// Catalogue centralisé des erreurs du sous-domaine "Orders".
/// Centraliser évite les chaînes magiques dupliquées et facilite les tests.
/// </summary>
public static class OrderErrors
{
    public static Error NotFound(Guid id) =>
        Error.NotFound("Orders.NotFound", $"La commande '{id}' est introuvable.");

    public static readonly Error CustomerNameRequired =
        Error.Validation("Orders.CustomerNameRequired", "Le nom du client est obligatoire.");

    public static readonly Error CustomerNameTooLong =
        Error.Validation("Orders.CustomerNameTooLong", $"Le nom du client ne doit pas dépasser {Order.CustomerNameMaxLength} caractères.");

    public static readonly Error NoLines =
        Error.Validation("Orders.NoLines", "Une commande doit contenir au moins une ligne.");

    public static readonly Error ProductNameRequired =
        Error.Validation("Orders.ProductNameRequired", "Le nom du produit est obligatoire.");

    public static readonly Error InvalidQuantity =
        Error.Validation("Orders.InvalidQuantity", "La quantité doit être strictement positive.");

    public static readonly Error InvalidUnitPrice =
        Error.Validation("Orders.InvalidUnitPrice", "Le prix unitaire ne peut pas être négatif.");

    public static Error InvalidTransition(OrderStatus from, OrderStatus to) =>
        Error.Conflict("Orders.InvalidTransition", $"Impossible de passer une commande de '{from}' à '{to}'.");

    public static Error NotModifiable(OrderStatus status) =>
        Error.Conflict("Orders.NotModifiable", $"Une commande au statut '{status}' ne peut plus être modifiée.");

    public static Error NotDeletable(OrderStatus status) =>
        Error.Conflict("Orders.NotDeletable", $"Une commande au statut '{status}' ne peut pas être supprimée.");
}
