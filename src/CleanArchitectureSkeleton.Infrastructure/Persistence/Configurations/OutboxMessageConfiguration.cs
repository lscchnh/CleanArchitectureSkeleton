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

        // Mêmes raisons que dans OrderConfiguration : SQLite ne trie/compare pas nativement les DateTimeOffset.
        builder.Property(m => m.OccurredOnUtc)
            .HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        builder.Property(m => m.ProcessedOnUtc)
            .HasConversion(v => v.HasValue ? v.Value.UtcTicks : (long?)null,
                           v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);

        // Index pour que l'OutboxProcessor ne scanne pas toute la table à chaque tick.
        builder.HasIndex(m => m.ProcessedOnUtc);
    }
}
