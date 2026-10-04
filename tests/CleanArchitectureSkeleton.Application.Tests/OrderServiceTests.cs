using CleanArchitectureSkeleton.Application.Orders;
using CleanArchitectureSkeleton.Application.Tests.Fakes;
using CleanArchitectureSkeleton.Domain.Common;
using CleanArchitectureSkeleton.Domain.Orders;

namespace CleanArchitectureSkeleton.Application.Tests;

/// <summary>
/// Tests du service applicatif : on vérifie l'ORCHESTRATION (transaction, verrou, cache, mapping des erreurs),
/// pas les règles métier (déjà testées dans Domain.Tests).
/// </summary>
public class OrderServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryOrderRepository _repository = new();
    private readonly FakeCacheService _cache = new();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly FixedTimeProvider _time = new(Now);
    private readonly OrderService _service;

    public OrderServiceTests()
    {
        _unitOfWork = new FakeUnitOfWork(_repository);
        _service = new OrderService(_repository, _unitOfWork, _cache, _time);
    }

    private static CreateOrderRequest ValidRequest(string customer = "Alice") =>
        new(customer, [new OrderLineRequest("Clavier", 2, 50m), new OrderLineRequest("Souris", 1, 20m)]);

    private async Task<OrderDto> SeedAsync()
    {
        var created = await _service.CreateAsync(ValidRequest());
        return created.Value;
    }

    // ── Create ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_persists_the_order_in_a_committed_transaction()
    {
        var result = await _service.CreateAsync(ValidRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(120m, result.Value.Total);
        Assert.Equal(OrderStatus.Pending, result.Value.Status);
        Assert.Equal(Now, result.Value.CreatedAt);
        Assert.Contains(result.Value.Id, _repository.Store.Keys);
        Assert.Equal(1, _unitOfWork.Commits);
    }

    [Fact]
    public async Task Create_with_invalid_line_fails_and_rolls_back()
    {
        var request = new CreateOrderRequest("Alice", [new OrderLineRequest("Clavier", 0, 50m)]);

        var result = await _service.CreateAsync(request);

        Assert.Equal(OrderErrors.InvalidQuantity, result.Error);
        Assert.Empty(_repository.Store);
        Assert.Equal(1, _unitOfWork.Rollbacks);
        Assert.Equal(0, _unitOfWork.Commits);
    }

    [Fact]
    public async Task Create_without_lines_fails()
    {
        var result = await _service.CreateAsync(new CreateOrderRequest("Alice", null));

        Assert.Equal(OrderErrors.NoLines, result.Error);
    }

    [Fact]
    public async Task Create_without_customer_fails()
    {
        var result = await _service.CreateAsync(ValidRequest(customer: " "));

        Assert.Equal(OrderErrors.CustomerNameRequired, result.Error);
    }

    // ── Get + cache ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_returns_the_order()
    {
        var created = await SeedAsync();

        var result = await _service.GetByIdAsync(created.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(created.Id, result.Value.Id);
        Assert.Equal(2, result.Value.Lines.Count);
    }

    [Fact]
    public async Task Get_unknown_order_returns_NotFound_and_is_not_cached()
    {
        var id = Guid.NewGuid();

        var result = await _service.GetByIdAsync(id);

        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(OrderErrors.NotFound(id), result.Error);
        Assert.False(_cache.Contains($"orders:{id:N}"));
    }

    [Fact]
    public async Task Get_hits_the_database_only_once_thanks_to_the_cache()
    {
        var created = await SeedAsync();

        await _service.GetByIdAsync(created.Id);
        await _service.GetByIdAsync(created.Id);
        await _service.GetByIdAsync(created.Id);

        Assert.Equal(1, _repository.GetByIdCalls);
        Assert.Equal(1, _cache.FactoryCalls);
    }

    // ── List ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_paginates_and_filters_by_status()
    {
        for (var i = 0; i < 5; i++)
        {
            _time.Now = Now.AddMinutes(i);
            await _service.CreateAsync(ValidRequest($"Client {i}"));
        }

        var confirmed = (await _service.CreateAsync(ValidRequest("Confirmé"))).Value;
        await _service.ConfirmAsync(confirmed.Id);

        var page1 = (await _service.ListAsync(1, 4, null)).Value;
        var pending = (await _service.ListAsync(1, 10, OrderStatus.Pending)).Value;

        Assert.Equal(6, page1.TotalCount);
        Assert.Equal(4, page1.Items.Count);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(5, pending.TotalCount);
        Assert.DoesNotContain(pending.Items, o => o.Id == confirmed.Id);
    }

    [Fact]
    public async Task List_sanitizes_out_of_range_paging_arguments()
    {
        var result = (await _service.ListAsync(-3, 100_000, null)).Value;

        Assert.Equal(1, result.Page);
        Assert.Equal(OrderService.MaxPageSize, result.PageSize);
    }

    // ── Update ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_uses_the_pessimistic_lock_and_invalidates_the_cache()
    {
        var created = await SeedAsync();
        await _service.GetByIdAsync(created.Id); // remplit le cache
        _time.Now = Now.AddHours(1);

        var result = await _service.UpdateAsync(created.Id,
            new UpdateOrderRequest("Bob", [new OrderLineRequest("Écran", 1, 200m)]));

        Assert.True(result.IsSuccess);
        Assert.Equal("Bob", result.Value.CustomerName);
        Assert.Equal(200m, result.Value.Total);
        Assert.Equal(Now.AddHours(1), result.Value.UpdatedAt);
        Assert.Equal(1, _repository.GetForUpdateCalls);          // lecture "for update"
        Assert.Contains($"orders:{created.Id:N}", _cache.Removed); // cache invalidé
        Assert.False(_cache.Contains($"orders:{created.Id:N}"));
    }

    [Fact]
    public async Task Update_unknown_order_returns_NotFound()
    {
        var id = Guid.NewGuid();

        var result = await _service.UpdateAsync(id, new UpdateOrderRequest("Bob", [new OrderLineRequest("X", 1, 1m)]));

        Assert.Equal(OrderErrors.NotFound(id), result.Error);
        Assert.Empty(_cache.Removed);
    }

    [Fact]
    public async Task Update_with_invalid_data_keeps_the_stored_order_and_the_cache()
    {
        var created = await SeedAsync();
        await _service.GetByIdAsync(created.Id);

        var result = await _service.UpdateAsync(created.Id, new UpdateOrderRequest("Bob", []));

        Assert.Equal(OrderErrors.NoLines, result.Error);
        Assert.Equal("Alice", (await _service.GetByIdAsync(created.Id)).Value.CustomerName);
        Assert.Empty(_cache.Removed); // pas de modification ⇒ pas d'invalidation inutile
    }

    [Fact]
    public async Task Update_of_a_confirmed_order_is_a_conflict()
    {
        var created = await SeedAsync();
        await _service.ConfirmAsync(created.Id);

        var result = await _service.UpdateAsync(created.Id,
            new UpdateOrderRequest("Bob", [new OrderLineRequest("X", 1, 1m)]));

        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    // ── Transitions ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_then_ship_follows_the_state_machine()
    {
        var created = await SeedAsync();

        var confirmed = await _service.ConfirmAsync(created.Id);
        var shipped = await _service.ShipAsync(created.Id);

        Assert.Equal(OrderStatus.Confirmed, confirmed.Value.Status);
        Assert.Equal(OrderStatus.Shipped, shipped.Value.Status);
    }

    [Fact]
    public async Task Ship_a_pending_order_is_a_conflict()
    {
        var created = await SeedAsync();

        var result = await _service.ShipAsync(created.Id);

        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(OrderStatus.Pending, (await _service.GetByIdAsync(created.Id)).Value.Status);
    }

    [Fact]
    public async Task Cancel_updates_status()
    {
        var created = await SeedAsync();

        var result = await _service.CancelAsync(created.Id);

        Assert.Equal(OrderStatus.Cancelled, result.Value.Status);
    }

    [Fact]
    public async Task Status_change_is_visible_on_next_read_because_cache_was_invalidated()
    {
        var created = await SeedAsync();
        await _service.GetByIdAsync(created.Id); // cache = Pending

        await _service.ConfirmAsync(created.Id);

        Assert.Equal(OrderStatus.Confirmed, (await _service.GetByIdAsync(created.Id)).Value.Status);
    }

    // ── Delete ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_removes_the_order_and_invalidates_the_cache()
    {
        var created = await SeedAsync();
        await _service.GetByIdAsync(created.Id);

        var result = await _service.DeleteAsync(created.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(_repository.Store);
        Assert.Equal(OrderErrors.NotFound(created.Id), (await _service.GetByIdAsync(created.Id)).Error);
    }

    [Fact]
    public async Task Delete_unknown_order_returns_NotFound()
    {
        var id = Guid.NewGuid();

        Assert.Equal(OrderErrors.NotFound(id), (await _service.DeleteAsync(id)).Error);
    }

    [Fact]
    public async Task Delete_of_a_shipped_order_is_a_conflict()
    {
        var created = await SeedAsync();
        await _service.ConfirmAsync(created.Id);
        await _service.ShipAsync(created.Id);

        var result = await _service.DeleteAsync(created.Id);

        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(_repository.Store);
    }
}
