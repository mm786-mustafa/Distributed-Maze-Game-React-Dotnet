using DistributedMazeGame.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributedMazeGame.Server.Data.Configurations
{
    public class MoveConfiguration : IEntityTypeConfiguration<Move>
    {
        public void Configure(EntityTypeBuilder<Move> builder)
        {
            builder.HasKey(m => m.MoveId);

            builder.Property(m => m.Direction).HasMaxLength(10).IsRequired();

            builder.HasOne(m => m.Player)
                   .WithMany(p => p.Moves)
                   .HasForeignKey(m => m.PlayerId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(m => m.Session)
                   .WithMany(s => s.Moves)
                   .HasForeignKey(m => m.SessionId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(m => new { m.PlayerId, m.SessionId });
            builder.HasIndex(m => new { m.SessionId, m.Timestamp });
        }
    }
}
