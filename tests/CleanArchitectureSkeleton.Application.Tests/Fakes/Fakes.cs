using CleanArchitectureSkeleton.Application.Abstractions;
using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Tests.Fakes;

// Des FAKES écrits à la main plutôt que des mocks : ils se comportent comme les vraies dépendances (en mémoire),
// donc les tests vérifient un COMPORTEMENT observable (« la commande est bien sauvegardée ») et non des appels de méthodes.
// C'est rendu possible par la Clean Architecture : le service ne dépend que d'interfaces (ports).

/// <summary>Repository en mémoire. Comme EF Core, les changements ne sont "visibles" qu'au commit de l'unité de travail.</summary>
public sealed class InMemoryOrderRepository : IOrderRepository
{
    public Dictionary<Guid, Order> Store { get; } = [];
    public List<Order> PendingAdds { get; } = [];
    public List<Order> PendingRemoves { get; } = [];
    public int GetByIdCalls { get; private set; }
    public int GetForUpdateCalls { get; private set; }

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        GetByIdCalls++;
        return Task.FromResult(Store.GetValueOrDefault(id));
    }

    public Task<Order?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        GetForUpdateCalls++;
        return Task.FromResult(Store.GetValueOrDefault(id));
    }

    public Task<(IReadOnlyList<Order> Items, int TotalCount)> ListAsync(
        int page, int pageSize, OrderStatus? status, CancellationToken cancellationToken = default)
    {
        var query = Store.Values.Where(o => status is null || o.Status == status)
            .OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).ToList();
        IReadOnlyList<Order> items = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((items, query.Count));
    }

    public void Add(Order order) => PendingAdds.Add(order);

    public void Remove(Order order) => PendingRemoves.Add(order);

    public void Commit()
    {
        foreach (var order in PendingAdds)
        {
            Store[order.Id] = order;
        }

        foreach (var order in PendingRemoves)
        {
            Store.Remove(order.Id);
        }

        Rollback();
    }

    public void Rollback()
    {
        PendingAdds.Clear();
        PendingRemoves.Clear();
    }
}

/// <summary>Unité de travail factice : commit si succès, rollback sinon — le même contrat que la vraie.</summary>
public sealed class FakeUnitOfWork(InMemoryOrderRepository repository) : IUnitOfWork
{
    public int Commits { get; private set; }
    public int Rollbacks { get; private set; }

    public async Task<Result<T>> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken = default)
    {
        var result = await work(cancellationToken);
        Complete(result);
        return result;
    }

    public async Task<Result> ExecuteInTransactionAsync(
        Func<CancellationToken, Task<Result>> work, CancellationToken cancellationToken = default)
    {
        var result = await work(cancellationToken);
        Complete(result);
        return result;
    }

    private void Complete(Result result)
    {
        if (result.IsSuccess)
        {
            repository.Commit();
            Commits++;
        }
        else
        {
            repository.Rollback();
            Rollbacks++;
        }
    }
}

/// <summary>Cache en mémoire qui compte ses appels, pour vérifier cache-aside et invalidation.</summary>
public sealed class FakeCacheService : ICacheService
{
    private readonly Dictionary<string, object> _entries = [];

    public int FactoryCalls { get; private set; }
    public List<string> Removed { get; } = [];
    public bool Contains(string key) => _entries.ContainsKey(key);

    public async Task<T?> GetOrCreateAsync<T>(
        string key, Func<CancellationToken, Task<T?>> factory, TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (_entries.TryGetValue(key, out var cached))
        {
            return (T)cached;
        }

        FactoryCalls++;
        var value = await factory(cancellationToken);
        if (value is not null)
        {
            _entries[key] = value;
        }

        return value;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _entries.Remove(key);
        Removed.Add(key);
        return Task.CompletedTask;
    }
}

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
