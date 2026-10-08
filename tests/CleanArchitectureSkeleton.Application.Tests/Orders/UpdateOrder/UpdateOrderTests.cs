using CleanArchitectureSkeleton.Application.Orders;
using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Tests.Orders;

/// <summary>Tests de la slice UpdateOrder : verrou pessimiste, invalidation du cache, et règles de transition.</summary>
public class UpdateOrderTests : OrderServiceTestBase
{
    [Fact]
    public async Task Update_uses_the_pessimistic_lock_and_invalidates_the_cache()
    {
        var created = await SeedAsync();
        await Service.GetByIdAsync(created.Id); // remplit le cache
        Time.Now = Now.AddHours(1);

        var result = await Service.UpdateAsync(created.Id,
            new UpdateOrderRequest("Bob", [new OrderLineRequest("Écran", 1, 200m)]));

        Assert.True(result.IsSuccess);
        Assert.Equal("Bob", result.Value.CustomerName);
        Assert.Equal(200m, result.Value.Total);
        Assert.Equal(Now.AddHours(1), result.Value.UpdatedAt);
        Assert.Equal(1, Repository.GetForUpdateCalls);          // lecture "for update"
        Assert.Contains($"orders:{created.Id:N}", Cache.Removed); // cache invalidé
        Assert.False(Cache.Contains($"orders:{created.Id:N}"));
    }

    [Fact]
    public async Task Update_unknown_order_returns_NotFound()
    {
        var id = Guid.NewGuid();

        var result = await Service.UpdateAsync(id, new UpdateOrderRequest("Bob", [new OrderLineRequest("X", 1, 1m)]));

        Assert.Equal(OrderErrors.NotFound(id), result.Error);
        Assert.Empty(Cache.Removed);
    }

    [Fact]
    public async Task Update_with_invalid_data_keeps_the_stored_order_and_the_cache()
    {
        var created = await SeedAsync();
        await Service.GetByIdAsync(created.Id);

        var result = await Service.UpdateAsync(created.Id, new UpdateOrderRequest("Bob", []));

        Assert.Equal(OrderErrors.NoLines, result.Error);
        Assert.Equal("Alice", (await Service.GetByIdAsync(created.Id)).Value.CustomerName);
        Assert.Empty(Cache.Removed); // pas de modification ⇒ pas d'invalidation inutile
    }

    [Fact]
    public async Task Update_of_a_confirmed_order_is_a_conflict()
    {
        var created = await SeedAsync();
        await Service.ConfirmAsync(created.Id);

        var result = await Service.UpdateAsync(created.Id,
            new UpdateOrderRequest("Bob", [new OrderLineRequest("X", 1, 1m)]));

        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }
}
