using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Tests.Orders;

/// <summary>Tests de la slice GetOrder : cache-aside et mapping NotFound.</summary>
public class GetOrderTests : OrderServiceTestBase
{
    [Fact]
    public async Task Get_returns_the_order()
    {
        var created = await SeedAsync();

        var result = await Service.GetByIdAsync(created.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(created.Id, result.Value.Id);
        Assert.Equal(2, result.Value.Lines.Count);
    }

    [Fact]
    public async Task Get_unknown_order_returns_NotFound_and_is_not_cached()
    {
        var id = Guid.NewGuid();

        var result = await Service.GetByIdAsync(id);

        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(OrderErrors.NotFound(id), result.Error);
        Assert.False(Cache.Contains($"orders:{id:N}"));
    }

    [Fact]
    public async Task Get_hits_the_database_only_once_thanks_to_the_cache()
    {
        var created = await SeedAsync();

        await Service.GetByIdAsync(created.Id);
        await Service.GetByIdAsync(created.Id);
        await Service.GetByIdAsync(created.Id);

        Assert.Equal(1, Repository.GetByIdCalls);
        Assert.Equal(1, Cache.FactoryCalls);
    }
}
