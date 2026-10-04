using CleanArchitectureSkeleton.Domain.Common;

namespace CleanArchitectureSkeleton.Domain.Orders;

/// <summary>
/// Ligne de commande. C'est un objet-valeur (immuable) qui n'existe qu'à l'intérieur de l'agrégat <see cref="Order"/> :
/// on ne la charge ni ne la modifie jamais directement, toujours via la commande (racine d'agrégat).
/// </summary>
public sealed record OrderLine
{
    public const int ProductNameMaxLength = 200;

    // Constructeur privé : on force le passage par la fabrique Create qui valide les invariants.
    private OrderLine(string productName, int quantity, decimal unitPrice)
    {
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public string ProductName { get; private init; }
    public int Quantity { get; private init; }
    public decimal UnitPrice { get; private init; }

    public decimal LineTotal => Quantity * UnitPrice;

    public static Result<OrderLine> Create(string? productName, int quantity, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return OrderErrors.ProductNameRequired;
        }

        if (quantity <= 0)
        {
            return OrderErrors.InvalidQuantity;
        }

        if (unitPrice < 0)
        {
            return OrderErrors.InvalidUnitPrice;
        }

        return new OrderLine(productName.Trim(), quantity, decimal.Round(unitPrice, 2));
    }
}
