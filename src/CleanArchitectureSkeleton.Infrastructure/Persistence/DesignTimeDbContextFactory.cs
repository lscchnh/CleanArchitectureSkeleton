using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CleanArchitectureSkeleton.Infrastructure.Persistence;

/// <summary>
/// Utilisée UNIQUEMENT par l'outil <c>dotnet ef</c> (création de migrations) :
/// il peut ainsi fonctionner sans démarrer toute l'application.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=orders-db;Username=postgres;Password=postgres")
            .Options;
        return new AppDbContext(options);
    }
}
