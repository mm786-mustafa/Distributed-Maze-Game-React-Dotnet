// GameLogic/GameSessionState.cs
using System.Collections.Generic;

public class GameSessionState
{
    // Shared state owned by GameWorker thread
    public Dictionary<int, (int x, int y)> PlayerPositions { get; } = new();
    public int[,] Maze { get; set; } = default!;
    public bool Completed { get; set; } = false;

    // Non-blocking snapshot read; guarded with lightweight lock
    private readonly object _snapshotLock = new();
    public int[,] SnapshotMaze()
    {
        lock (_snapshotLock)
        {
            var copy = (int[,])Maze.Clone();
            return copy;
        }
    }
}
