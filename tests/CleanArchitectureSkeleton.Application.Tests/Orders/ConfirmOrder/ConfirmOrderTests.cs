using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Tests.Orders;

/// <summary>Tests de la slice ConfirmOrder : transition d'état et invalidation du cache.</summary>
public class ConfirmOrderTests : OrderServiceTestBase
{
    [Fact]
    public async Task Confirm_then_ship_follows_the_state_machine()
    {
        var created = await SeedAsync();

        var confirmed = await Service.ConfirmAsync(created.Id);
        var shipped = await Service.ShipAsync(created.Id);

        Assert.Equal(OrderStatus.Confirmed, confirmed.Value.Status);
        Assert.Equal(OrderStatus.Shipped, shipped.Value.Status);
    }

    [Fact]
    public async Task Status_change_is_visible_on_next_read_because_cache_was_invalidated()
    {
        var created = await SeedAsync();
        await Service.GetByIdAsync(created.Id); // cache = Pending

        await Service.ConfirmAsync(created.Id);

        Assert.Equal(OrderStatus.Confirmed, (await Service.GetByIdAsync(created.Id)).Value.Status);
    }
}
