using CleanArchitectureSkeleton.Application.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitectureSkeleton.Application;

public static class DependencyInjection
{
    /// <summary>Chaque couche expose sa propre méthode d'enregistrement : le point d'entrée (API) ne fait que les appeler.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IOrderService, OrderService>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
