namespace DistributedMazeGame.Server.Data.Entities
{
    public class GameSession
    {
        public int SessionId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Status { get; set; } = "Pending";

        // Navigation
        public ICollection<Move> Moves { get; set; } = new List<Move>();
        public Result? Result { get; set; }
    }
}
