using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Domain.Tests.Orders;

public class OrderLineTests
{
    [Fact]
    public void Create_with_valid_data_succeeds_and_computes_total()
    {
        var result = OrderLine.Create("  Clavier  ", 3, 10.005m);

        Assert.True(result.IsSuccess);
        Assert.Equal("Clavier", result.Value.ProductName);      // espaces retirés
        Assert.Equal(10.00m, result.Value.UnitPrice);           // arrondi à 2 décimales (banker's rounding)
        Assert.Equal(30.00m, result.Value.LineTotal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_without_product_name_fails(string? name)
    {
        var result = OrderLine.Create(name, 1, 1m);

        Assert.Equal(OrderErrors.ProductNameRequired, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_with_non_positive_quantity_fails(int quantity)
    {
        var result = OrderLine.Create("Produit", quantity, 1m);

        Assert.Equal(OrderErrors.InvalidQuantity, result.Error);
    }

    [Fact]
    public void Create_with_negative_price_fails()
    {
        var result = OrderLine.Create("Produit", 1, -0.01m);

        Assert.Equal(OrderErrors.InvalidUnitPrice, result.Error);
    }

    [Fact]
    public void Free_item_is_allowed()
    {
        Assert.True(OrderLine.Create("Échantillon", 1, 0m).IsSuccess);
    }
}
