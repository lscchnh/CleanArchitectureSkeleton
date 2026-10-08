using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Orders;

// DTO = Data Transfer Object. On n'expose JAMAIS les entités du Domain à l'extérieur :
// le contrat de l'API (DTO) peut ainsi évoluer indépendamment du modèle métier.
// Modèle de LECTURE partagé par toutes les slices "Orders" (pas spécifique à un cas d'usage).

public sealed record OrderLineDto(string ProductName, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record OrderDto(
    Guid Id,
    string CustomerName,
    OrderStatus Status,
    decimal Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<OrderLineDto> Lines);
