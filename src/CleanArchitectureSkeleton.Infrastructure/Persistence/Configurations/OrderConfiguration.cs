using CleanArchitectureSkeleton.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitectureSkeleton.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);

        // L'identifiant est généré par le Domain (Guid.NewGuid), pas par la base.
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.CustomerName).IsRequired().HasMaxLength(Order.CustomerNameMaxLength);
        // L'enum est stocké en texte : plus lisible en base et robuste si on réordonne l'enum.
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        // PostgreSQL gère nativement DateTimeOffset (colonne timestamptz) : aucune conversion nécessaire.

        // Total est une propriété calculée du Domain : on ne la persiste pas.
        builder.Ignore(o => o.Total);

        // Les DomainEvents sont transitoires (vidés par EfUnitOfWork après copie en Outbox) : jamais persistés.
        builder.Ignore(o => o.DomainEvents);

        // Les lignes sont un "owned type" : leur cycle de vie est lié à la commande (table séparée, clé étrangère, cascade).
        builder.OwnsMany(o => o.Lines, lines =>
        {
            lines.ToTable("OrderLines");
            lines.WithOwner().HasForeignKey("OrderId");
            lines.Property<int>("Id");
            lines.HasKey("Id");
            lines.Property(l => l.ProductName).IsRequired().HasMaxLength(OrderLine.ProductNameMaxLength);
            lines.Property(l => l.Quantity);
            // decimal(18,2) natif PostgreSQL : pas de perte de précision.
            lines.Property(l => l.UnitPrice).HasPrecision(18, 2);
            lines.Ignore(l => l.LineTotal);
        });

        // EF doit remplir le champ privé _lines plutôt que la propriété en lecture seule.
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Index pour la liste filtrée par statut et triée par date.
        builder.HasIndex(o => new { o.Status, o.CreatedAt });
    }
}
