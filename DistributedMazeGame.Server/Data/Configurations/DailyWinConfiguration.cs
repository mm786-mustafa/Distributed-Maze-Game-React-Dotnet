// =============================================================================
// Data/Configurations/DailyWinConfiguration.cs
// =============================================================================
// Entity Framework Core configuration for DailyWin entity
// 
// PDC CONCEPT: Database indexing for distributed query optimization
// Proper indexes are critical for leaderboard queries to remain performant
// as the data grows. The composite unique index ensures data integrity
// while the date index enables efficient daily leaderboard retrieval.
// =============================================================================

using DistributedMazeGame.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributedMazeGame.Server.Data.Configurations
{
    public class DailyWinConfiguration : IEntityTypeConfiguration<DailyWin>
    {
        public void Configure(EntityTypeBuilder<DailyWin> builder)
        {
            builder.HasKey(x => x.DailyWinId);

            builder.Property(x => x.Date)
                .IsRequired();

            builder.Property(x => x.WinCount)
                .IsRequired();

            builder.Property(x => x.TotalFlagsCaptured)
                .IsRequired()
                .HasDefaultValue(0);

            builder.Property(x => x.GamesPlayed)
                .IsRequired()
                .HasDefaultValue(0);

            builder.Property(x => x.LastUpdated)
                .IsRequired();

            // Foreign key relationship
            builder.HasOne(x => x.Player)
                .WithMany(p => p.DailyWins)
                .HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);

            // CRITICAL: Composite unique index to ensure one record per player per day
            // This is enforced at the database level for data integrity
            builder.HasIndex(x => new { x.PlayerId, x.Date })
                .IsUnique();

            // Index for daily leaderboard queries (most important for performance)
            // Queries like: SELECT * FROM DailyWins WHERE Date = @today ORDER BY WinCount DESC
            builder.HasIndex(x => new { x.Date, x.WinCount });
            
            // Index for player history queries
            builder.HasIndex(x => x.PlayerId);
        }
    }
}
