using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Domain.Tests.Orders;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(1);

    private static OrderLine Line(string product = "Produit", int quantity = 1, decimal price = 10m) =>
        OrderLine.Create(product, quantity, price).Value;

    private static Order NewOrder() => Order.Create("Alice", [Line(), Line("Autre", 2, 5m)], Now).Value;

    // ── Création ────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_builds_a_pending_order_with_computed_total()
    {
        var result = Order.Create("  Alice  ", [Line(quantity: 2, price: 10m), Line(price: 5m)], Now);

        Assert.True(result.IsSuccess);
        var order = result.Value;
        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Equal("Alice", order.CustomerName);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(Now, order.CreatedAt);
        Assert.Null(order.UpdatedAt);
        Assert.Equal(25m, order.Total);
        Assert.Equal(2, order.Lines.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_requires_a_customer_name(string? name)
    {
        Assert.Equal(OrderErrors.CustomerNameRequired, Order.Create(name, [Line()], Now).Error);
    }

    [Fact]
    public void Create_rejects_too_long_customer_name()
    {
        var name = new string('x', Order.CustomerNameMaxLength + 1);

        Assert.Equal(OrderErrors.CustomerNameTooLong, Order.Create(name, [Line()], Now).Error);
    }

    [Fact]
    public void Create_requires_at_least_one_line()
    {
        Assert.Equal(OrderErrors.NoLines, Order.Create("Alice", [], Now).Error);
    }

    // ── Modification ────────────────────────────────────────────────────────────

    [Fact]
    public void Update_replaces_content_while_pending()
    {
        var order = NewOrder();

        var result = order.Update("Bob", [Line(quantity: 4, price: 2m)], Later);

        Assert.True(result.IsSuccess);
        Assert.Equal("Bob", order.CustomerName);
        Assert.Single(order.Lines);
        Assert.Equal(8m, order.Total);
        Assert.Equal(Later, order.UpdatedAt);
    }

    [Fact]
    public void Update_with_invalid_data_leaves_the_order_untouched()
    {
        var order = NewOrder();

        var result = order.Update("", [Line()], Later);

        Assert.Equal(OrderErrors.CustomerNameRequired, result.Error);
        Assert.Equal("Alice", order.CustomerName);
        Assert.Equal(2, order.Lines.Count);
        Assert.Null(order.UpdatedAt);
    }

    [Fact]
    public void Update_is_forbidden_once_confirmed()
    {
        var order = NewOrder();
        order.Confirm(Later);

        var result = order.Update("Bob", [Line()], Later);

        Assert.Equal(OrderErrors.NotModifiable(OrderStatus.Confirmed), result.Error);
    }

    // ── Machine à états ─────────────────────────────────────────────────────────

    [Fact]
    public void Happy_path_pending_confirmed_shipped()
    {
        var order = NewOrder();

        Assert.True(order.Confirm(Later).IsSuccess);
        Assert.Equal(OrderStatus.Confirmed, order.Status);

        Assert.True(order.Ship(Later).IsSuccess);
        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.Equal(Later, order.UpdatedAt);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    public void Cancel_is_allowed_before_shipping(OrderStatus from)
    {
        var order = NewOrder();
        if (from == OrderStatus.Confirmed)
        {
            order.Confirm(Now);
        }

        Assert.True(order.Cancel(Later).IsSuccess);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cannot_ship_a_pending_order()
    {
        var order = NewOrder();

        var result = order.Ship(Later);

        Assert.Equal(OrderErrors.InvalidTransition(OrderStatus.Pending, OrderStatus.Shipped), result.Error);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void Cannot_cancel_a_shipped_order()
    {
        var order = NewOrder();
        order.Confirm(Now);
        order.Ship(Now);

        Assert.True(order.Cancel(Later).IsFailure);
        Assert.Equal(OrderStatus.Shipped, order.Status);
    }

    [Fact]
    public void Cancelled_is_a_final_state()
    {
        var order = NewOrder();
        order.Cancel(Now);

        Assert.True(order.Confirm(Later).IsFailure);
        Assert.True(order.Cancel(Later).IsFailure);
    }

    [Fact]
    public void Cannot_confirm_twice()
    {
        var order = NewOrder();
        order.Confirm(Now);

        Assert.Equal(
            OrderErrors.InvalidTransition(OrderStatus.Confirmed, OrderStatus.Confirmed),
            order.Confirm(Later).Error);
    }

    // ── Suppression ─────────────────────────────────────────────────────────────

    [Fact]
    public void Shipped_order_is_not_deletable()
    {
        var order = NewOrder();
        order.Confirm(Now);
        order.Ship(Now);

        Assert.Equal(OrderErrors.NotDeletable(OrderStatus.Shipped), order.EnsureDeletable().Error);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled)]
    public void Pending_or_cancelled_order_is_deletable(OrderStatus status)
    {
        var order = NewOrder();
        if (status == OrderStatus.Cancelled)
        {
            order.Cancel(Now);
        }

        Assert.True(order.EnsureDeletable().IsSuccess);
    }

    [Fact]
    public void Lines_are_exposed_read_only()
    {
        var order = NewOrder();

        Assert.IsNotType<List<OrderLine>>(order.Lines);
    }
}
