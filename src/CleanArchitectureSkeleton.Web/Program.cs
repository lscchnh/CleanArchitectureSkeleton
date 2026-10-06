using CleanArchitectureSkeleton.Web.Components;
using CleanArchitectureSkeleton.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Aspire : service discovery, résilience HTTP, OpenTelemetry, health checks par défaut (mêmes defaults que l'Api).
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Nom logique "api" résolu par Aspire (service discovery) vers l'instance réelle de CleanArchitectureSkeleton.Api.
builder.Services.AddHttpClient<OrdersApiClient>(client => client.BaseAddress = new Uri("https+http://api"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapDefaultEndpoints(); // /health (readiness) et /alive (liveness)

app.Run();
