using CleanArchitectureSkeleton.Application.Abstractions;
using CleanArchitectureSkeleton.Domain.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitectureSkeleton.Infrastructure.Tests;

/// <summary>Tests d'INTÉGRATION : repository + EF Core + vraie base PostgreSQL (migrations appliquées).</summary>
public class OrderRepositoryTests : IAsyncLifetime
{
    private PostgresTestHost _host = null!;

    public async Task InitializeAsync() => _host = await PostgresTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static Order NewOrder(string customer = "Alice", DateTimeOffset? at = null) =>
        Order.Create(customer,
            [OrderLine.Create("Clavier", 2, 49.90m).Value, OrderLine.Create("Souris", 1, 20m).Value],
            at ?? DateTimeOffset.UtcNow).Value;

    /// <summary>Chaque appel crée un scope = un DbContext neuf, comme une requête HTTP distincte (aucun cache EF partagé).</summary>
    private async Task WriteAsync(params Order[] orders)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var result = await uow.ExecuteInTransactionAsync(_ =>
        {
            foreach (var order in orders)
            {
                repo.Add(order);
            }

            return Task.FromResult(Domain.Common.Result.Success());
        });
        Assert.True(result.IsSuccess);
    }

    private async Task<T> ReadAsync<T>(Func<IOrderRepository, Task<T>> read)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<IOrderRepository>());
    }

    [Fact]
    public async Task Added_order_round_trips_with_its_lines()
    {
        var order = NewOrder();
        await WriteAsync(order);

        var loaded = await ReadAsync(r => r.GetByIdAsync(order.Id));

        Assert.NotNull(loaded);
        Assert.Equal("Alice", loaded.CustomerName);
        Assert.Equal(OrderStatus.Pending, loaded.Status);
        Assert.Equal(order.CreatedAt.UtcTicks, loaded.CreatedAt.UtcTicks);
        Assert.Equal(2, loaded.Lines.Count);
        Assert.Equal(49.90m, loaded.Lines.First().UnitPrice);
        Assert.Equal(order.Total, loaded.Total);
    }

    [Fact]
    public async Task Unknown_id_returns_null()
    {
        Assert.Null(await ReadAsync(r => r.GetByIdAsync(Guid.NewGuid())));
    }

    [Fact]
    public async Task List_is_paginated_newest_first_and_filterable()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var orders = Enumerable.Range(0, 5).Select(i => NewOrder($"Client {i}", baseTime.AddMinutes(i))).ToArray();
        orders[4].Confirm(baseTime);
        await WriteAsync(orders);

        var (page1, total) = await ReadAsync(r => r.ListAsync(1, 2, null));
        var (page3, _) = await ReadAsync(r => r.ListAsync(3, 2, null));
        var (pending, pendingTotal) = await ReadAsync(r => r.ListAsync(1, 10, OrderStatus.Pending));

        Assert.Equal(5, total);
        Assert.Equal(["Client 4", "Client 3"], page1.Select(o => o.CustomerName));
        Assert.Equal(["Client 0"], page3.Select(o => o.CustomerName));
        Assert.Equal(4, pendingTotal);
        Assert.DoesNotContain(pending, o => o.CustomerName == "Client 4");
    }

    [Fact]
    public async Task Update_and_delete_are_persisted()
    {
        var order = NewOrder();
        await WriteAsync(order);

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                var tracked = (await repo.GetByIdForUpdateAsync(order.Id, ct))!;
                return tracked.Update("Bob", [OrderLine.Create("Écran", 1, 199m).Value], DateTimeOffset.UtcNow);
            });
        }

        var updated = await ReadAsync(r => r.GetByIdAsync(order.Id));
        Assert.Equal("Bob", updated!.CustomerName);
        Assert.Equal("Écran", Assert.Single(updated.Lines).ProductName); // anciennes lignes supprimées

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
            await uow.ExecuteInTransactionAsync(async ct =>
            {
                repo.Remove((await repo.GetByIdForUpdateAsync(order.Id, ct))!);
                return Domain.Common.Result.Success();
            });
        }

        Assert.Null(await ReadAsync(r => r.GetByIdAsync(order.Id)));
    }
}
