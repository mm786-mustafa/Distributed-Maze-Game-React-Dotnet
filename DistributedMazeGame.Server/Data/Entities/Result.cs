namespace DistributedMazeGame.Server.Data.Entities
{
    public class Result
    {
        public int ResultId { get; set; }
        public int SessionId { get; set; }
        public int WinnerPlayerId { get; set; }
        public int Duration { get; set; }

        // Navigation
        public GameSession Session { get; set; } = null!;
        public Player Winner { get; set; } = null!;
    }
}
