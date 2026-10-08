using CleanArchitectureSkeleton.Application.Orders;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Tests.Orders;

/// <summary>Tests de la slice CreateOrder : orchestration (transaction), pas les règles métier (Domain.Tests).</summary>
public class CreateOrderTests : OrderServiceTestBase
{
    [Fact]
    public async Task Create_persists_the_order_in_a_committed_transaction()
    {
        var result = await Service.CreateAsync(ValidRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(120m, result.Value.Total);
        Assert.Equal(OrderStatus.Pending, result.Value.Status);
        Assert.Equal(Now, result.Value.CreatedAt);
        Assert.Contains(result.Value.Id, Repository.Store.Keys);
        Assert.Equal(1, UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_with_invalid_line_fails_and_rolls_back()
    {
        var request = new CreateOrderRequest("Alice", [new OrderLineRequest("Clavier", 0, 50m)]);

        var result = await Service.CreateAsync(request);

        Assert.Equal(OrderErrors.InvalidQuantity, result.Error);
        Assert.Empty(Repository.Store);
        Assert.Equal(1, UnitOfWork.Rollbacks);
        Assert.Equal(0, UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_without_lines_fails()
    {
        var result = await Service.CreateAsync(new CreateOrderRequest("Alice", null));

        Assert.Equal(OrderErrors.NoLines, result.Error);
    }

    [Fact]
    public async Task Create_without_customer_fails()
    {
        var result = await Service.CreateAsync(ValidRequest(customer: " "));

        Assert.Equal(OrderErrors.CustomerNameRequired, result.Error);
    }
}
