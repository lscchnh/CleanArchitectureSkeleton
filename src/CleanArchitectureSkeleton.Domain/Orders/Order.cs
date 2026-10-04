using CleanArchitectureSkeleton.Domain.Common;

namespace CleanArchitectureSkeleton.Domain.Orders;

/// <summary>
/// Agrégat racine "Commande". Le Domain est le cœur de la Clean Architecture :
/// il ne dépend de RIEN (ni EF Core, ni ASP.NET, ni base de données).
/// Toutes les règles métier (invariants, transitions d'état) vivent ici, pas dans les services ni les contrôleurs :
/// c'est ce qu'on appelle un modèle riche (par opposition à un modèle anémique).
/// </summary>
public sealed class Order
{
    public const int CustomerNameMaxLength = 200;

    private readonly List<OrderLine> _lines = [];

    // Constructeur privé sans paramètre réservé à EF Core (matérialisation depuis la base).
    private Order()
    {
    }

    public Guid Id { get; private set; }
    public string CustomerName { get; private set; } = string.Empty;
    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Lecture seule vers l'extérieur : seule la commande peut modifier ses lignes.</summary>
    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    public decimal Total => _lines.Sum(l => l.LineTotal);

    /// <summary>
    /// Fabrique : seule façon de créer une commande valide.
    /// L'heure est passée en paramètre (et non DateTime.UtcNow) pour garder le Domain pur et testable.
    /// </summary>
    public static Result<Order> Create(string? customerName, IEnumerable<OrderLine> lines, DateTimeOffset now)
    {
        var validation = Validate(customerName, lines);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = customerName!.Trim(),
            Status = OrderStatus.Pending,
            CreatedAt = now,
        };
        order._lines.AddRange(lines);
        return order;
    }

    /// <summary>Modification du contenu : autorisée uniquement tant que la commande est en attente.</summary>
    public Result Update(string? customerName, IEnumerable<OrderLine> lines, DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending)
        {
            return Result.Failure(OrderErrors.NotModifiable(Status));
        }

        var lineList = lines.ToList();
        var validation = Validate(customerName, lineList);
        if (validation.IsFailure)
        {
            return validation;
        }

        CustomerName = customerName!.Trim();
        _lines.Clear();
        _lines.AddRange(lineList);
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Confirm(DateTimeOffset now) => TransitionTo(OrderStatus.Confirmed, now);

    public Result Ship(DateTimeOffset now) => TransitionTo(OrderStatus.Shipped, now);

    public Result Cancel(DateTimeOffset now) => TransitionTo(OrderStatus.Cancelled, now);

    /// <summary>Règle métier : on ne supprime que ce qui n'a pas encore été expédié.</summary>
    public Result EnsureDeletable() =>
        Status is OrderStatus.Shipped
            ? Result.Failure(OrderErrors.NotDeletable(Status))
            : Result.Success();

    private Result TransitionTo(OrderStatus target, DateTimeOffset now)
    {
        // Table des transitions autorisées (machine à états).
        var allowed = (Status, target) switch
        {
            (OrderStatus.Pending, OrderStatus.Confirmed) => true,
            (OrderStatus.Confirmed, OrderStatus.Shipped) => true,
            (OrderStatus.Pending, OrderStatus.Cancelled) => true,
            (OrderStatus.Confirmed, OrderStatus.Cancelled) => true,
            _ => false,
        };

        if (!allowed)
        {
            return Result.Failure(OrderErrors.InvalidTransition(Status, target));
        }

        Status = target;
        UpdatedAt = now;
        return Result.Success();
    }

    private static Result Validate(string? customerName, IEnumerable<OrderLine> lines)
    {
        if (string.IsNullOrWhiteSpace(customerName))
        {
            return Result.Failure(OrderErrors.CustomerNameRequired);
        }

        if (customerName.Trim().Length > CustomerNameMaxLength)
        {
            return Result.Failure(OrderErrors.CustomerNameTooLong);
        }

        return lines.Any() ? Result.Success() : Result.Failure(OrderErrors.NoLines);
    }
}
