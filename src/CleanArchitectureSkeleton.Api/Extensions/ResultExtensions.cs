using CleanArchitectureSkeleton.Domain.Common;

namespace CleanArchitectureSkeleton.Api.Extensions;

/// <summary>
/// Traduit un <see cref="Result"/> du Domain/Application en réponse HTTP.
/// C'est LA frontière : seule la couche de présentation connaît HTTP. Le reste de l'application
/// parle en <see cref="ErrorType"/>, et c'est ici qu'on décide « NotFound ⇒ 404 ».
/// Les erreurs sont renvoyées au format standard RFC 9457 (ProblemDetails).
/// </summary>
public static class ResultExtensions
{
    public static IResult ToProblem(this Error error) => Results.Problem(
        statusCode: error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        },
        title: error.Code,
        detail: error.Description);

    /// <summary>Succès ⇒ <paramref name="onSuccess"/>, échec ⇒ ProblemDetails. Les deux branches sont obligatoires.</summary>
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.Match(onSuccess, ToProblem);

    public static IResult ToHttpResult(this Result result, Func<IResult> onSuccess) =>
        result.IsSuccess ? onSuccess() : result.Error.ToProblem();
}
