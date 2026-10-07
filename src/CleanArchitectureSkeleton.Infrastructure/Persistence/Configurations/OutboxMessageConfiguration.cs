using CleanArchitectureSkeleton.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitectureSkeleton.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Content).IsRequired();

        // PostgreSQL gère nativement DateTimeOffset (colonne timestamptz) : aucune conversion nécessaire.
        builder.Property(m => m.OccurredOnUtc);
        builder.Property(m => m.ProcessedOnUtc);

        // Index pour que l'OutboxProcessor ne scanne pas toute la table à chaque tick.
        builder.HasIndex(m => m.ProcessedOnUtc);
    }
}
