using CleanArchitectureSkeleton.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace CleanArchitectureSkeleton.Infrastructure.Persistence;

/// <summary>
/// Détail d'infrastructure : EF Core et SQLite n'existent QUE dans cette couche.
/// Remplacer SQLite par PostgreSQL ou SQL Server ne touche donc ni le Domain ni l'Application.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        // Le mapping est rangé dans des classes IEntityTypeConfiguration, pour ne pas polluer les entités du Domain
        // avec des attributs d'infrastructure ([Table], [MaxLength]...).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
