namespace DistributedMazeGame.Server.Data.Entities
{
    /// <summary>
    /// Represents a single game session/match in the distributed maze game.
    /// Multiple players participate in each session, competing to capture flags.
    /// </summary>
    public class GameSession
    {
        public int SessionId { get; set; }
        
        /// <summary>
        /// When the game started
        /// </summary>
        public DateTime StartTime { get; set; }
        
        /// <summary>
        /// When the game ended (null if still in progress)
        /// </summary>
        public DateTime? EndTime { get; set; }
        
        /// <summary>
        /// Current status: "Pending", "Active", "Completed", "Cancelled"
        /// </summary>
        public string Status { get; set; } = "Pending";
        
        /// <summary>
        /// Total number of flags that were captured in this session
        /// </summary>
        public int TotalFlagsCaptured { get; set; }
        
        /// <summary>
        /// Number of players who participated
        /// </summary>
        public int PlayerCount { get; set; }

        // Navigation properties
        public ICollection<Move> Moves { get; set; } = new List<Move>();
        public Result? Result { get; set; }
        
        /// <summary>
        /// Individual player scores for this session
        /// </summary>
        public ICollection<PlayerScore> PlayerScores { get; set; } = new List<PlayerScore>();
    }
}
