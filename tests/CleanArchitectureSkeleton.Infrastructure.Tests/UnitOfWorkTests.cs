using CleanArchitectureSkeleton.Application.Abstractions;
using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitectureSkeleton.Infrastructure.Tests;

/// <summary>Transactions et concurrency control pessimiste, sur une vraie base.</summary>
public class UnitOfWorkTests : IAsyncLifetime
{
    private SqliteTestHost _host = null!;

    public async Task InitializeAsync() => _host = await SqliteTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static Order NewOrder(string customer = "A") =>
        Order.Create(customer, [OrderLine.Create("Produit", 1, 10m).Value], DateTimeOffset.UtcNow).Value;

    private async Task<Order?> LoadAsync(Guid id)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOrderRepository>().GetByIdAsync(id);
    }

    [Fact]
    public async Task Successful_work_is_committed()
    {
        var order = NewOrder();
        await using var scope = _host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var result = await uow.ExecuteInTransactionAsync(_ =>
        {
            repo.Add(order);
            return Task.FromResult(Result.Success());
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(await LoadAsync(order.Id));
    }

    [Fact]
    public async Task Failed_result_rolls_back_everything()
    {
        var order = NewOrder();
        await using var scope = _host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var result = await uow.ExecuteInTransactionAsync<int>(_ =>
        {
            repo.Add(order); // marqué pour insertion...
            return Task.FromResult<Result<int>>(Error.Conflict("Test.Boom", "échec métier")); // ...mais le travail échoue
        });

        Assert.Equal("Test.Boom", result.Error.Code);
        Assert.Null(await LoadAsync(order.Id));
    }

    [Fact]
    public async Task Exception_rolls_back_and_propagates()
    {
        var order = NewOrder();
        await using var scope = _host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        Func<CancellationToken, Task<Result>> failing = _ =>
        {
            repo.Add(order);
            throw new InvalidOperationException("bug");
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteInTransactionAsync(failing));

        Assert.Null(await LoadAsync(order.Id));
    }

    [Fact]
    public async Task Atomicity_two_orders_are_saved_together_or_not_at_all()
    {
        var first = NewOrder("first");
        var second = NewOrder("second");
        await using var scope = _host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        await uow.ExecuteInTransactionAsync(_ =>
        {
            repo.Add(first);
            repo.Add(second);
            return Task.FromResult(Result.Failure(Error.Failure("Test.Late", "échec après deux ajouts")));
        });

        Assert.Null(await LoadAsync(first.Id));
        Assert.Null(await LoadAsync(second.Id));
    }

    [Fact]
    public async Task Unit_of_work_is_reusable_after_a_failure()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        Func<CancellationToken, Task<Result>> failing = _ => throw new InvalidOperationException("boom");
        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteInTransactionAsync(failing));

        var order = NewOrder();
        var result = await uow.ExecuteInTransactionAsync(_ =>
        {
            repo.Add(order);
            return Task.FromResult(Result.Success());
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(await LoadAsync(order.Id));
    }

    /// <summary>
    /// LE test du verrou pessimiste. 12 "utilisateurs" lisent la MÊME commande et lui ajoutent chacun un caractère,
    /// en même temps, avec une pause volontaire entre lecture et écriture pour maximiser les entrelacements.
    /// • Avec le verrou : les transactions sont sérialisées ⇒ aucune mise à jour perdue ⇒ 12 caractères ajoutés.
    /// • Sans verrou : plusieurs lisent la même valeur et s'écrasent mutuellement ("lost update") ⇒ moins de 12.
    /// </summary>
    [Fact]
    public async Task Pessimistic_lock_serializes_concurrent_updates_without_lost_updates()
    {
        const int concurrentWriters = 12;
        var order = NewOrder("S");
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(_ =>
            {
                scope.ServiceProvider.GetRequiredService<IOrderRepository>().Add(order);
                return Task.FromResult(Result.Success());
            });
        }

        var tasks = Enumerable.Range(0, concurrentWriters).Select(_ => Task.Run(async () =>
        {
            // Un scope par "requête" : chaque writer a son propre DbContext et sa propre connexion.
            await using var scope = _host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

            return await uow.ExecuteInTransactionAsync(async ct =>
            {
                var current = (await repo.GetByIdForUpdateAsync(order.Id, ct))!;
                await Task.Delay(30, ct); // fenêtre où un autre writer s'intercalerait SANS verrou
                return current.Update(current.CustomerName + "x", current.Lines, DateTimeOffset.UtcNow);
            });
        }));

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.IsSuccess));
        var final = await LoadAsync(order.Id);
        Assert.Equal(1 + concurrentWriters, final!.CustomerName.Length); // "S" + 12 x
    }
}
