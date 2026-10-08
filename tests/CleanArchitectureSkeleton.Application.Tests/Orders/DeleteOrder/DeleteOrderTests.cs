using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Tests.Orders;

/// <summary>Tests de la slice DeleteOrder : suppression, NotFound, et refus si déjà expédiée.</summary>
public class DeleteOrderTests : OrderServiceTestBase
{
    [Fact]
    public async Task Delete_removes_the_order_and_invalidates_the_cache()
    {
        var created = await SeedAsync();
        await Service.GetByIdAsync(created.Id);

        var result = await Service.DeleteAsync(created.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(Repository.Store);
        Assert.Equal(OrderErrors.NotFound(created.Id), (await Service.GetByIdAsync(created.Id)).Error);
    }

    [Fact]
    public async Task Delete_unknown_order_returns_NotFound()
    {
        var id = Guid.NewGuid();

        Assert.Equal(OrderErrors.NotFound(id), (await Service.DeleteAsync(id)).Error);
    }

    [Fact]
    public async Task Delete_of_a_shipped_order_is_a_conflict()
    {
        var created = await SeedAsync();
        await Service.ConfirmAsync(created.Id);
        await Service.ShipAsync(created.Id);

        var result = await Service.DeleteAsync(created.Id);

        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(Repository.Store);
    }
}
