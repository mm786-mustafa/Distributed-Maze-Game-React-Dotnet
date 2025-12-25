namespace DistributedMazeGame.Server.Data.Entities
{
    /// <summary>
    /// Represents a player in the distributed maze game.
    /// Players can participate in multiple game sessions and accumulate wins.
    /// </summary>
    public class Player
    {
        public int PlayerId { get; set; }
        
        /// <summary>
        /// Display name chosen by the player
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// When the player first connected to the system
        /// </summary>
        public DateTime ConnectedAt { get; set; }

        // Navigation properties
        public ICollection<Move> Moves { get; set; } = new List<Move>();
        public ICollection<Result> Results { get; set; } = new List<Result>();
        
        /// <summary>
        /// All score records for this player across all games
        /// </summary>
        public ICollection<PlayerScore> PlayerScores { get; set; } = new List<PlayerScore>();
        
        /// <summary>
        /// Daily win aggregates for leaderboard tracking
        /// </summary>
        public ICollection<DailyWin> DailyWins { get; set; } = new List<DailyWin>();
    }
}
