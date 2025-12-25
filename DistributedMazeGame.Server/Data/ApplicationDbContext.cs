using Microsoft.EntityFrameworkCore;
using DistributedMazeGame.Server.Data.Entities;

namespace DistributedMazeGame.Server.Data
{
    /// <summary>
    /// Entity Framework Core database context for the Distributed Maze Game.
    /// 
    /// PDC CONCEPT: Centralized data persistence layer
    /// All game state that needs to survive server restarts is persisted here.
    /// The DbContextFactory pattern allows parallel database operations.
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        /// <summary>
        /// All registered players
        /// </summary>
        public DbSet<Player> Players { get; set; }
        
        /// <summary>
        /// All game sessions (matches)
        /// </summary>
        public DbSet<GameSession> GameSessions { get; set; }
        
        /// <summary>
        /// Movement history for replay/analysis
        /// </summary>
        public DbSet<Move> Moves { get; set; }
        
        /// <summary>
        /// Game results (winner info)
        /// </summary>
        public DbSet<Result> Results { get; set; }
        
        /// <summary>
        /// Individual player scores per game session
        /// </summary>
        public DbSet<PlayerScore> PlayerScores { get; set; }
        
        /// <summary>
        /// Pre-aggregated daily win counts for leaderboard
        /// </summary>
        public DbSet<DailyWin> DailyWins { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder) 
        { 
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly); 
            base.OnModelCreating(modelBuilder); 
        }
    }
}
