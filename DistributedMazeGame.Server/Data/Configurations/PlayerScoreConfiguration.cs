// =============================================================================
// Data/Configurations/PlayerScoreConfiguration.cs
// =============================================================================
// Entity Framework Core configuration for PlayerScore entity
// =============================================================================

using DistributedMazeGame.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributedMazeGame.Server.Data.Configurations
{
    public class PlayerScoreConfiguration : IEntityTypeConfiguration<PlayerScore>
    {
        public void Configure(EntityTypeBuilder<PlayerScore> builder)
        {
            builder.HasKey(x => x.PlayerScoreId);

            builder.Property(x => x.PlayerName)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(x => x.FlagsCaptured)
                .IsRequired();

            builder.Property(x => x.IsWinner)
                .IsRequired();

            builder.Property(x => x.FinalRank)
                .IsRequired();

            builder.Property(x => x.RecordedAt)
                .IsRequired();

            // Foreign key relationships
            builder.HasOne(x => x.Session)
                .WithMany(s => s.PlayerScores)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Player)
                .WithMany(p => p.PlayerScores)
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Index for querying player's game history
            builder.HasIndex(x => x.PlayerId);
            
            // Index for querying session results
            builder.HasIndex(x => x.SessionId);
            
            // Composite index for date-based queries (e.g., daily stats)
            builder.HasIndex(x => x.RecordedAt);
            
            // Index for finding winners efficiently
            builder.HasIndex(x => new { x.IsWinner, x.RecordedAt });
        }
    }
}
