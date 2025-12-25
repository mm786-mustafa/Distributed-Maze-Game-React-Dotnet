// =============================================================================
// Data/Entities/DailyWin.cs
// =============================================================================
// DAILY WIN ENTITY - Aggregated daily win tracking for leaderboard
// 
// PDC CONCEPT: Pre-aggregated data for efficient distributed queries
// Instead of computing daily wins on every query (which would be expensive),
// we maintain a denormalized aggregate table that's updated atomically
// when games complete. This follows the CQRS pattern where read and write
// models are optimized separately.
// =============================================================================

namespace DistributedMazeGame.Server.Data.Entities
{
    /// <summary>
    /// Tracks the number of wins a player has accumulated on a specific date.
    /// This is a denormalized aggregate table for efficient leaderboard queries.
    /// 
    /// PDC PATTERN: Materialized View / Pre-computed Aggregate
    /// - Avoids expensive GROUP BY queries on every leaderboard request
    /// - Updated atomically when games end (eventual consistency is acceptable)
    /// </summary>
    public class DailyWin
    {
        public int DailyWinId { get; set; }
        
        /// <summary>
        /// Foreign key to the player
        /// </summary>
        public int PlayerId { get; set; }
        
        /// <summary>
        /// The date (without time) for this win count.
        /// Indexed for efficient daily queries.
        /// </summary>
        public DateOnly Date { get; set; }
        
        /// <summary>
        /// Number of games won by this player on this date
        /// </summary>
        public int WinCount { get; set; }
        
        /// <summary>
        /// Total flags captured across all games on this date
        /// (for secondary ranking/stats)
        /// </summary>
        public int TotalFlagsCaptured { get; set; }
        
        /// <summary>
        /// Number of games played on this date (wins + losses)
        /// </summary>
        public int GamesPlayed { get; set; }
        
        /// <summary>
        /// Last update timestamp (for debugging/auditing)
        /// </summary>
        public DateTime LastUpdated { get; set; }

        // Navigation property
        public Player Player { get; set; } = null!;
    }
}
