using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Tests.Orders;

/// <summary>Tests de la slice CancelOrder.</summary>
public class CancelOrderTests : OrderServiceTestBase
{
    [Fact]
    public async Task Cancel_updates_status()
    {
        var created = await SeedAsync();

        var result = await Service.CancelAsync(created.Id);

        Assert.Equal(OrderStatus.Cancelled, result.Value.Status);
    }
}
