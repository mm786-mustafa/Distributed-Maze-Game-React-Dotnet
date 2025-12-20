namespace DistributedMazeGame.Server.Data.Entities
{
    public class Move
    {
        public long MoveId { get; set; }
        public int PlayerId { get; set; }
        public int SessionId { get; set; }
        public string Direction { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }

        // Navigation
        public Player Player { get; set; } = null!;
        public GameSession Session { get; set; } = null!;
    }
}
