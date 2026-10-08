using CleanArchitectureSkeleton.Application.Orders;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Tests.Orders;

/// <summary>Tests de la slice ListOrders : pagination et filtrage par statut.</summary>
public class ListOrdersTests : OrderServiceTestBase
{
    [Fact]
    public async Task List_paginates_and_filters_by_status()
    {
        for (var i = 0; i < 5; i++)
        {
            Time.Now = Now.AddMinutes(i);
            await Service.CreateAsync(ValidRequest($"Client {i}"));
        }

        var confirmed = (await Service.CreateAsync(ValidRequest("Confirmé"))).Value;
        await Service.ConfirmAsync(confirmed.Id);

        var page1 = (await Service.ListAsync(1, 4, null)).Value;
        var pending = (await Service.ListAsync(1, 10, OrderStatus.Pending)).Value;

        Assert.Equal(6, page1.TotalCount);
        Assert.Equal(4, page1.Items.Count);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(5, pending.TotalCount);
        Assert.DoesNotContain(pending.Items, o => o.Id == confirmed.Id);
    }

    [Fact]
    public async Task List_sanitizes_out_of_range_paging_arguments()
    {
        var result = (await Service.ListAsync(-3, 100_000, null)).Value;

        Assert.Equal(1, result.Page);
        Assert.Equal(OrderService.MaxPageSize, result.PageSize);
    }
}
