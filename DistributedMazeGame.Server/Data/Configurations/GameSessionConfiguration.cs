using DistributedMazeGame.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributedMazeGame.Server.Data.Configurations
{
    public class GameSessionConfiguration : IEntityTypeConfiguration<GameSession>
    {
        public void Configure(EntityTypeBuilder<GameSession> builder)
        {
            builder.HasKey(s => s.SessionId);
            builder.Property(s => s.Status).HasMaxLength(20).IsRequired();
            builder.HasIndex(s => s.Status);
            builder.HasIndex(s => s.StartTime);
        }
    }
}
