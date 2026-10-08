using CleanArchitectureSkeleton.Application.Orders;
using CleanArchitectureSkeleton.Application.Tests.Fakes;

namespace CleanArchitectureSkeleton.Application.Tests.Orders;

/// <summary>
/// Socle commun à tous les tests de slices "Orders" : même service, mêmes fakes, même donnée de seed.
/// Chaque classe dérivée se concentre sur UNE SEULE slice (CreateOrder, ConfirmOrder, ...), à l'image
/// de l'organisation du code de production (un dossier par cas d'usage).
/// </summary>
public abstract class OrderServiceTestBase
{
    protected static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    protected InMemoryOrderRepository Repository { get; } = new();
    protected FakeCacheService Cache { get; } = new();
    protected FakeUnitOfWork UnitOfWork { get; }
    protected FixedTimeProvider Time { get; } = new(Now);
    protected OrderService Service { get; }

    protected OrderServiceTestBase()
    {
        UnitOfWork = new FakeUnitOfWork(Repository);
        Service = new OrderService(Repository, UnitOfWork, Cache, Time);
    }

    protected static CreateOrderRequest ValidRequest(string customer = "Alice") =>
        new(customer, [new OrderLineRequest("Clavier", 2, 50m), new OrderLineRequest("Souris", 1, 20m)]);

    protected async Task<OrderDto> SeedAsync()
    {
        var created = await Service.CreateAsync(ValidRequest());
        return created.Value;
    }
}
