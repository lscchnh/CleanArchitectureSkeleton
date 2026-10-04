namespace CleanArchitectureSkeleton.Domain.Common;

/// <summary>
/// Catégorie technique d'une erreur. Elle permet à la couche la plus externe (l'API)
/// de choisir le bon code HTTP SANS que le Domain ne connaisse HTTP.
/// </summary>
public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unavailable = 4,
}

/// <summary>
/// Une erreur est une VALEUR (record immuable), pas une exception.
/// Les erreurs "attendues" du métier (commande introuvable, quantité invalide...)
/// ne sont pas exceptionnelles : on les modélise donc explicitement avec le pattern Result.
/// </summary>
/// <param name="Code">Code stable lisible par une machine, ex: "Orders.NotFound".</param>
/// <param name="Description">Message lisible par un humain.</param>
/// <param name="Type">Catégorie de l'erreur (voir <see cref="ErrorType"/>).</param>
public sealed record Error(string Code, string Description, ErrorType Type)
{
    /// <summary>Valeur sentinelle utilisée par un Result en succès.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
    public static Error Unavailable(string code, string description) => new(code, description, ErrorType.Unavailable);
}
