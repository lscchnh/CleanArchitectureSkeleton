using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Tests.Orders;

/// <summary>Tests de la slice ShipOrder : refus de transition si la commande n'est pas confirmée.</summary>
public class ShipOrderTests : OrderServiceTestBase
{
    [Fact]
    public async Task Ship_a_pending_order_is_a_conflict()
    {
        var created = await SeedAsync();

        var result = await Service.ShipAsync(created.Id);

        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(OrderStatus.Pending, (await Service.GetByIdAsync(created.Id)).Value.Status);
    }
}
