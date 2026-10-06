using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CleanArchitectureSkeleton.Application.Orders;
using CleanArchitectureSkeleton.Domain.Orders;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureSkeleton.Web.Services;

/// <summary>Levée quand l'Api répond avec un ProblemDetails (RFC 9457) : le message vient directement de l'erreur Domain/Application.</summary>
public sealed class OrdersApiException(string message) : Exception(message);

/// <summary>
/// Fine couche HTTP vers CleanArchitectureSkeleton.Api : la page Blazor ne parle jamais directement à HttpClient.
/// Le nom "api" dans le BaseAddress (voir Program.cs) est résolu par le service discovery d'Aspire.
/// </summary>
public sealed class OrdersApiClient(HttpClient httpClient)
{
    // L'Api sérialise OrderStatus en texte (voir ConfigureHttpJsonOptions dans Api/Program.cs) : le client doit utiliser le même contrat.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<PagedResult<OrderDto>> ListAsync(int page, int pageSize, OrderStatus? status, CancellationToken ct = default)
    {
        var query = $"/api/orders?page={page}&pageSize={pageSize}" + (status is null ? "" : $"&status={status}");
        var result = await httpClient.GetFromJsonAsync<PagedResult<OrderDto>>(query, JsonOptions, ct);
        return result ?? new PagedResult<OrderDto>([], page, pageSize, 0);
    }

    public Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken ct = default) =>
        SendAsync(() => httpClient.PostAsJsonAsync("/api/orders", request, ct));

    public Task<OrderDto> ConfirmAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(() => httpClient.PostAsync($"/api/orders/{id}/confirm", null, ct));

    public Task<OrderDto> ShipAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(() => httpClient.PostAsync($"/api/orders/{id}/ship", null, ct));

    public Task<OrderDto> CancelAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(() => httpClient.PostAsync($"/api/orders/{id}/cancel", null, ct));

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await httpClient.DeleteAsync($"/api/orders/{id}", ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new OrdersApiException(await ReadProblemDetailAsync(response, ct));
        }
    }

    private static async Task<OrderDto> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        using var response = await send();
        if (!response.IsSuccessStatusCode)
        {
            throw new OrdersApiException(await ReadProblemDetailAsync(response, CancellationToken.None));
        }

        return (await response.Content.ReadFromJsonAsync<OrderDto>(JsonOptions))!;
    }

    private static async Task<string> ReadProblemDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions, ct);
        return problem?.Detail ?? $"Erreur inattendue ({(int)response.StatusCode}).";
    }
}
