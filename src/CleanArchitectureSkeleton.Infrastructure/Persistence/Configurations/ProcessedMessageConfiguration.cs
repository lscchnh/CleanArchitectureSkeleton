using CleanArchitectureSkeleton.Infrastructure.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitectureSkeleton.Infrastructure.Persistence.Configurations;

internal sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("ProcessedMessages");
        // La clé primaire = Id du message Outbox d'origine : c'est CE QUI REND LE CONSOMMATEUR IDEMPOTENT.
        // Une insertion d'un Id déjà présent viole la contrainte d'unicité ; OutboxConsumer vérifie
        // l'existence AVANT d'insérer (voir commentaires dans OutboxConsumer.cs).
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Type).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Content).IsRequired();

        // PostgreSQL gère nativement DateTimeOffset (colonne timestamptz) : aucune conversion nécessaire.
        builder.Property(m => m.ProcessedOnUtc);

        // Sert à trier "les plus récents d'abord" pour l'encart Blazor sans scanner toute la table.
        builder.HasIndex(m => m.ProcessedOnUtc);
    }
}
