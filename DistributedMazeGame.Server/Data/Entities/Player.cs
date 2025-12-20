namespace DistributedMazeGame.Server.Data.Entities
{
    public class Player
    {
        public int PlayerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime ConnectedAt { get; set; }

        // Navigation
        public ICollection<Move> Moves { get; set; } = new List<Move>();
        public ICollection<Result> Results { get; set; } = new List<Result>();
    }
}
