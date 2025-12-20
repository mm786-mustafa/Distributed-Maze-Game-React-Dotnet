using DistributedMazeGame.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DistributedMazeGame.Server.Data.Configurations
{
    public class ResultConfiguration : IEntityTypeConfiguration<Result>
    {
        public void Configure(EntityTypeBuilder<Result> builder)
        {
            builder.HasKey(r => r.ResultId);

            builder.HasOne(r => r.Session)
                   .WithOne(s => s.Result)
                   .HasForeignKey<Result>(r => r.SessionId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(r => r.Winner)
                   .WithMany(p => p.Results)
                   .HasForeignKey(r => r.WinnerPlayerId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(r => r.WinnerPlayerId);
            builder.HasIndex(r => r.SessionId).IsUnique();
        }
    }
}
