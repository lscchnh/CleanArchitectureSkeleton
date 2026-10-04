using CleanArchitectureSkeleton.Domain.Common;

namespace CleanArchitectureSkeleton.Application.Abstractions;

/// <summary>
/// PORT (interface définie par la couche Application, implémentée par Infrastructure).
/// C'est l'inversion de dépendances : le service applicatif dit "j'ai besoin d'une transaction",
/// sans savoir si elle est fournie par EF Core, Dapper, SQLite ou SQL Server.
///
/// Contrat :
///  1. Ouvre une transaction qui prend un VERROU PESSIMISTE en écriture (voir l'implémentation).
///  2. Exécute <paramref name="work"/>.
///  3. Si le Result est un succès → sauvegarde les changements et commit ; sinon → rollback.
///  4. En cas de panne transitoire, TOUTE la transaction est rejouée : <paramref name="work"/> doit donc être
///     rejouable (relire ses données à chaque appel, ne pas dépendre d'un état externe modifié).
/// </summary>
public interface IUnitOfWork
{
    Task<Result<T>> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default);

    Task<Result> ExecuteInTransactionAsync(
        Func<CancellationToken, Task<Result>> work,
        CancellationToken cancellationToken = default);
}
