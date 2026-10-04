using System.Reflection;
using CleanArchitectureSkeleton.Application.Orders;
using CleanArchitectureSkeleton.Domain.Orders;
using NetArchTest.Rules;

namespace CleanArchitectureSkeleton.Architecture.Tests;

/// <summary>
/// Tests d'ARCHITECTURE : ils font respecter automatiquement la règle de dépendance de la Clean Architecture.
/// Si quelqu'un ajoute un `using Microsoft.EntityFrameworkCore;` dans le Domain, la CI échoue : l'architecture
/// ne peut plus se dégrader silencieusement avec le temps.
///
///     Api ──► Infrastructure ──► Application ──► Domain      (les dépendances pointent TOUJOURS vers l'intérieur)
/// </summary>
public class DependencyRuleTests
{
    private const string Root = "CleanArchitectureSkeleton";
    private const string DomainNs = $"{Root}.Domain";
    private const string ApplicationNs = $"{Root}.Application";
    private const string InfrastructureNs = $"{Root}.Infrastructure";
    private const string ApiNs = $"{Root}.Api";

    private static readonly Assembly Domain = typeof(Order).Assembly;
    private static readonly Assembly Application = typeof(IOrderService).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static void AssertNoDependencies(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).Should().NotHaveDependencyOnAny(forbidden).GetResult();

        Assert.True(result.IsSuccessful,
            $"Dépendances interdites dans {assembly.GetName().Name} : {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_depends_on_nothing_but_the_BCL() =>
        AssertNoDependencies(Domain,
            ApplicationNs, InfrastructureNs, ApiNs,
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions", "Microsoft.Data", "Polly");

    [Fact]
    public void Application_does_not_depend_on_outer_layers_or_technologies() =>
        AssertNoDependencies(Application,
            InfrastructureNs, ApiNs,
            "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Data", "Polly");

    [Fact]
    public void Infrastructure_does_not_depend_on_the_presentation_layer() =>
        AssertNoDependencies(Infrastructure, ApiNs, "Microsoft.AspNetCore.Http", "Microsoft.AspNetCore.Mvc");

    [Fact]
    public void Api_does_not_touch_the_persistence_details()
    {
        // L'API ne doit pas connaître EF Core : elle passe par les services applicatifs.
        // (Elle référence Infrastructure uniquement pour le câblage DI dans Program.cs.)
        AssertNoDependencies(Api, "Microsoft.EntityFrameworkCore", "Microsoft.Data.Sqlite");
    }

    [Fact]
    public void Api_endpoints_only_use_application_services_not_repositories()
    {
        var result = Types.InAssembly(Api).That().ResideInNamespace($"{ApiNs}.Endpoints")
            .ShouldNot().HaveDependencyOnAny($"{ApplicationNs}.Abstractions", $"{InfrastructureNs}")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Infrastructure_implementations_are_internal()
    {
        // Seuls les points d'entrée DI sont publics : le reste est un détail d'implémentation inaccessible de l'extérieur,
        // ce qui empêche l'API d'instancier directement un repository.
        var result = Types.InAssembly(Infrastructure).That()
            .ImplementInterface(typeof(Application.Abstractions.IOrderRepository))
            .Or().ImplementInterface(typeof(Application.Abstractions.IUnitOfWork))
            .Or().ImplementInterface(typeof(Application.Abstractions.ICacheService))
            .Should().NotBePublic().GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Ports_are_interfaces_declared_in_the_application_layer()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespace($"{ApplicationNs}.Abstractions")
            .Should().BeInterfaces().GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Domain_entities_do_not_expose_public_setters()
    {
        // Modèle riche : l'état ne change que via des méthodes métier qui protègent les invariants.
        var offenders = new[] { typeof(Order), typeof(OrderLine) }
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.SetMethod is { IsPublic: true } && !p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Application_services_follow_the_naming_convention()
    {
        var result = Types.InAssembly(Application).That().ImplementInterface(typeof(IOrderService))
            .Should().HaveNameEndingWith("Service").GetResult();

        Assert.True(result.IsSuccessful);
    }
}
