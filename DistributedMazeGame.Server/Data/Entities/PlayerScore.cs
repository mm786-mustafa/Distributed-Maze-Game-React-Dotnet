// =============================================================================
// Data/Entities/PlayerScore.cs
// =============================================================================
// PLAYER SCORE ENTITY - Tracks individual player performance per game
// 
// PDC CONCEPT: Persistent state storage for distributed systems
// This entity stores the final score of each player in each game session,
// enabling historical analysis and leaderboard computation.
// =============================================================================

namespace DistributedMazeGame.Server.Data.Entities
{
    /// <summary>
    /// Records a player's final score in a specific game session.
    /// Each game session has multiple PlayerScore records (one per participant).
    /// </summary>
    public class PlayerScore
    {
        public int PlayerScoreId { get; set; }
        
        /// <summary>
        /// Foreign key to the game session
        /// </summary>
        public int SessionId { get; set; }
        
        /// <summary>
        /// Foreign key to the player
        /// </summary>
        public int PlayerId { get; set; }
        
        /// <summary>
        /// Player's display name at the time of the game
        /// (stored here to preserve historical accuracy even if player changes name)
        /// </summary>
        public string PlayerName { get; set; } = string.Empty;
        
        /// <summary>
        /// Number of flags captured by this player in this session
        /// </summary>
        public int FlagsCaptured { get; set; }
        
        /// <summary>
        /// Whether this player won the game (highest score)
        /// Multiple players can win in case of a tie
        /// </summary>
        public bool IsWinner { get; set; }
        
        /// <summary>
        /// Final rank of the player (1 = first place)
        /// </summary>
        public int FinalRank { get; set; }
        
        /// <summary>
        /// Timestamp when the game ended
        /// </summary>
        public DateTime RecordedAt { get; set; }

        // Navigation properties
        public GameSession Session { get; set; } = null!;
        public Player Player { get; set; } = null!;
    }
}
