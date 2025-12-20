using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pomelo.EntityFrameworkCore.MySql;

namespace DistributedMazeGame.Server.Data
{
    // Ensures dotnet-ef can create the DbContext without running Program.cs or connecting to MySQL for AutoDetect.
    public sealed class DesignTimeApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

            // Prefer an env var to avoid committing secrets; falls back to appsettings-like default.
            var connectionString = Environment.GetEnvironmentVariable("MAZE_DB")
                ?? "server=localhost;port=3306;database=MazeGame;user=root;password=corg;";

            // Explicit server version avoids AutoDetect (which requires a DB connection).
            var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
            optionsBuilder.UseMySql(connectionString, serverVersion);

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}
