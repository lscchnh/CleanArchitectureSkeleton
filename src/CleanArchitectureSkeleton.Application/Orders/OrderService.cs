using CleanArchitectureSkeleton.Application.Abstractions;
using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Orders;

/// <summary>
/// Orchestration des cas d'usage. Le service NE CONTIENT PAS de règle métier (elles sont dans <see cref="Order"/>) :
/// il enchaîne simplement « charger → demander au domaine → persister → invalider le cache ».
///
/// Organisation en VERTICAL SLICES : chaque cas d'usage (CreateOrder, ConfirmOrder, ...) vit dans son propre
/// dossier (son DTO dédié + son implémentation en <c>partial class</c>), au lieu d'un unique fichier de 150+ lignes
/// qui mélange tout. On garde volontairement UN SEUL service — pas de handler par classe, pas de MediatR :
/// <see cref="IOrderService"/> reste la façade unique vue par l'Api, donc rien ne change côté appelants/tests.
/// Seuls les helpers réellement PARTAGÉS entre plusieurs slices (verrou+commit, mapping DTO← écriture) vivent ici ;
/// le reste est dans le dossier de la slice qui l'utilise.
/// </summary>
public sealed partial class OrderService : IOrderService
{
    public const int MaxPageSize = 100;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IOrderRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly TimeProvider _timeProvider;

    public OrderService(IOrderRepository repository, IUnitOfWork unitOfWork, ICacheService cache, TimeProvider timeProvider)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _timeProvider = timeProvider;
    }

    private static string CacheKey(Guid id) => $"orders:{id:N}";

    /// <summary>
    /// Squelette commun aux slices de MODIFICATION (UpdateOrder, ConfirmOrder, ShipOrder, CancelOrder) :
    /// verrou pessimiste → règle métier → commit → invalidation du cache.
    /// </summary>
    private async Task<Result<OrderDto>> ModifyAsync(
        Guid id,
        Func<Order, DateTimeOffset, Result> mutation,
        CancellationToken cancellationToken)
    {
        var result = await _unitOfWork.ExecuteInTransactionAsync<OrderDto>(async ct =>
        {
            // Lecture "pour mise à jour" DANS la transaction : la commande est verrouillée jusqu'au commit.
            var order = await _repository.GetByIdForUpdateAsync(id, ct);
            if (order is null)
            {
                return OrderErrors.NotFound(id);
            }

            var outcome = mutation(order, _timeProvider.GetUtcNow());
            return outcome.IsFailure ? outcome.Error : order.ToDto();
        }, cancellationToken);

        if (result.IsSuccess)
        {
            // Invalider APRÈS le commit : si on invalidait avant, une lecture concurrente pourrait
            // remettre en cache l'ancienne valeur avant que la transaction ne soit visible.
            await _cache.RemoveAsync(CacheKey(id), cancellationToken);
        }

        return result;
    }

    /// <summary>Mapping écriture → Domain, partagé par CreateOrder et UpdateOrder (les deux seules slices qui écrivent des lignes).</summary>
    private static Result<List<OrderLine>> BuildLines(IReadOnlyList<OrderLineRequest>? requests)
    {
        var lines = new List<OrderLine>();
        foreach (var request in requests ?? [])
        {
            var line = OrderLine.Create(request.ProductName, request.Quantity, request.UnitPrice);
            if (line.IsFailure)
            {
                return line.Error;
            }

            lines.Add(line.Value);
        }

        return lines;
    }
}
