namespace CleanArchitectureSkeleton.Domain.Common;

/// <summary>
/// Pattern Result : le résultat d'une opération qui peut échouer, SANS lever d'exception.
/// Avantages : le flux d'erreur est visible dans la signature, le compilateur nous force à le traiter,
/// et on évite le coût et l'opacité des exceptions pour les cas métier prévisibles.
/// Les exceptions restent réservées aux situations réellement exceptionnelles (panne, bug).
/// </summary>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        // Invariant : un succès n'a pas d'erreur, un échec en a forcément une.
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("Un Result en succès ne peut pas porter d'erreur.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("Un Result en échec doit porter une erreur.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => new(value, true, Error.None);
    public static Result<T> Failure<T>(Error error) => new(default, false, error);
}

/// <summary>Result portant une valeur en cas de succès.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, bool isSuccess, Error error) : base(isSuccess, error) => _value = value;

    /// <summary>Accéder à la valeur d'un échec est un bug de programmation : on lève donc une exception.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Impossible de lire la valeur d'un Result en échec.");

    /// <summary>Conversion implicite pratique : <c>return order;</c> au lieu de <c>Result.Success(order)</c>.</summary>
    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure<T>(error);

    /// <summary>Transforme la valeur en cas de succès, propage l'erreur sinon (style "railway").</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        IsSuccess ? Success(mapper(Value)) : Failure<TOut>(Error);

    /// <summary>Force le traitement des deux branches : une seule expression, pas de oubli possible.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Error);
}
