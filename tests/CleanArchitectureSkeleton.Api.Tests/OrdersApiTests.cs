using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CleanArchitectureSkeleton.Application.Orders;
using CleanArchitectureSkeleton.Domain.Orders;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureSkeleton.Api.Tests;

/// <summary>Tests de bout en bout : HTTP → endpoint → service → repository → PostgreSQL, et retour.</summary>
public sealed class OrdersApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public OrdersApiTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static CreateOrderRequest Valid(string customer = "Alice") =>
        new(customer, [new OrderLineRequest("Clavier", 2, 50m), new OrderLineRequest("Souris", 1, 20m)]);

    private async Task<OrderDto> CreateAsync(string customer = "Alice")
    {
        var response = await _client.PostAsJsonAsync("/api/orders", Valid(customer));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OrderDto>(Json))!;
    }

    private static async Task<ProblemDetails> ProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;

    // ── Create / Read ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_creates_an_order_and_returns_201_with_location()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", Valid());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderDto>(Json))!;
        Assert.Equal($"/api/orders/{order.Id}", response.Headers.Location!.OriginalString);
        Assert.Equal(120m, order.Total);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task Get_returns_the_created_order()
    {
        var created = await CreateAsync();

        var response = await _client.GetAsync($"/api/orders/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderDto>(Json))!;
        Assert.Equal("Alice", order.CustomerName);
        Assert.Equal(2, order.Lines.Count);
    }

    [Fact]
    public async Task Get_unknown_order_returns_404_problem_details()
    {
        var response = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.Equal("Orders.NotFound", problem.Title);
    }

    [Fact]
    public async Task Get_with_a_malformed_id_does_not_match_a_route()
    {
        var response = await _client.GetAsync("/api/orders/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_invalid_payload_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/orders",
            new CreateOrderRequest("Alice", [new OrderLineRequest("Clavier", 0, 10m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Orders.InvalidQuantity", (await ProblemAsync(response)).Title);
    }

    [Fact]
    public async Task Post_without_customer_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", Valid(customer: ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Orders.CustomerNameRequired", (await ProblemAsync(response)).Title);
    }

    // ── List ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_is_paginated_and_filterable_by_status()
    {
        for (var i = 0; i < 3; i++)
        {
            await CreateAsync($"Client {i}");
        }

        var confirmed = await CreateAsync("Confirmé");
        await _client.PostAsync($"/api/orders/{confirmed.Id}/confirm", null);

        var page = (await _client.GetFromJsonAsync<PagedResult<OrderDto>>("/api/orders?page=1&pageSize=2", Json))!;
        var pending = (await _client.GetFromJsonAsync<PagedResult<OrderDto>>("/api/orders?status=Pending", Json))!;

        Assert.Equal(4, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(3, pending.TotalCount);
    }

    // ── Update + invalidation du cache ──────────────────────────────────────────

    [Fact]
    public async Task Put_updates_the_order_and_a_subsequent_get_is_not_served_stale_from_cache()
    {
        var created = await CreateAsync();
        await _client.GetAsync($"/api/orders/{created.Id}"); // met l'ancienne version en cache

        var put = await _client.PutAsJsonAsync($"/api/orders/{created.Id}",
            new UpdateOrderRequest("Bob", [new OrderLineRequest("Écran", 1, 199m)]));
        var fresh = (await _client.GetFromJsonAsync<OrderDto>($"/api/orders/{created.Id}", Json))!;

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("Bob", fresh.CustomerName);
        Assert.Equal(199m, fresh.Total);
        Assert.NotNull(fresh.UpdatedAt);
    }

    [Fact]
    public async Task Put_unknown_order_returns_404()
    {
        var response = await _client.PutAsJsonAsync($"/api/orders/{Guid.NewGuid()}",
            new UpdateOrderRequest("Bob", [new OrderLineRequest("X", 1, 1m)]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Cycle de vie ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Order_goes_through_its_lifecycle_and_invalid_transitions_return_409()
    {
        var created = await CreateAsync();

        var shipTooEarly = await _client.PostAsync($"/api/orders/{created.Id}/ship", null);
        var confirm = await _client.PostAsync($"/api/orders/{created.Id}/confirm", null);
        var updateAfterConfirm = await _client.PutAsJsonAsync($"/api/orders/{created.Id}",
            new UpdateOrderRequest("Bob", [new OrderLineRequest("X", 1, 1m)]));
        var ship = await _client.PostAsync($"/api/orders/{created.Id}/ship", null);
        var cancelAfterShip = await _client.PostAsync($"/api/orders/{created.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, shipTooEarly.StatusCode);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, updateAfterConfirm.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ship.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, cancelAfterShip.StatusCode);
        Assert.Equal("Orders.InvalidTransition", (await ProblemAsync(cancelAfterShip)).Title);
    }

    [Fact]
    public async Task Cancel_works_on_a_pending_order()
    {
        var created = await CreateAsync();

        var response = await _client.PostAsync($"/api/orders/{created.Id}/cancel", null);

        var order = (await response.Content.ReadFromJsonAsync<OrderDto>(Json))!;
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    // ── Delete ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_returns_204_then_the_order_is_gone_even_if_it_was_cached()
    {
        var created = await CreateAsync();
        await _client.GetAsync($"/api/orders/{created.Id}"); // cache

        var delete = await _client.DeleteAsync($"/api/orders/{created.Id}");
        var get = await _client.GetAsync($"/api/orders/{created.Id}");
        var deleteAgain = await _client.DeleteAsync($"/api/orders/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }

    [Fact]
    public async Task Delete_of_a_shipped_order_returns_409()
    {
        var created = await CreateAsync();
        await _client.PostAsync($"/api/orders/{created.Id}/confirm", null);
        await _client.PostAsync($"/api/orders/{created.Id}/ship", null);

        var response = await _client.DeleteAsync($"/api/orders/{created.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── Concurrence de bout en bout ─────────────────────────────────────────────

    [Fact]
    public async Task Concurrent_confirm_requests_yield_exactly_one_success()
    {
        var created = await CreateAsync();

        // 8 clients confirment la MÊME commande en même temps : le verrou pessimiste sérialise les transactions,
        // le 1er passe Pending→Confirmed, les 7 autres voient "déjà confirmée" ⇒ 409. Jamais deux succès.
        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => _client.PostAsync($"/api/orders/{created.Id}/confirm", null)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }
}
