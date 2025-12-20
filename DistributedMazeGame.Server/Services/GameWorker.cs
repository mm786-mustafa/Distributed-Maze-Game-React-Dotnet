// Services/GameWorker.cs
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

public class GameWorker : BackgroundService
{
    private readonly ConcurrentQueue<PlayerInput> _inputs;
    private readonly Channel<AcceptedMove> _moveLog;
    private readonly GameSessionState _state;

    public GameWorker(ConcurrentQueue<PlayerInput> inputs, Channel<AcceptedMove> moveLog, GameSessionState state)
    {
        _inputs = inputs;
        _moveLog = moveLog;
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Single thread owns mutations and win checks
        var tickInterval = TimeSpan.FromMilliseconds(33); // ~30 Hz
        while (!stoppingToken.IsCancellationRequested)
        {
            // Drain inputs
            while (_inputs.TryDequeue(out var input))
            {
                if (_state.Completed) continue;

                // Validate & apply
                var (nx, ny) = ApplyMove(_state, input.PlayerId, input.Direction);
                // Emit accepted move to logger
                var accepted = new AcceptedMove(SessionId: 1, PlayerId: input.PlayerId, Seq: input.Seq, Direction: input.Direction, ServerTime: DateTime.UtcNow);
                await _moveLog.Writer.WriteAsync(accepted, stoppingToken);
            }

            // Win condition
            if (CheckWin(_state))
            {
                _state.Completed = true;
                // emit match end, etc.
            }

            await Task.Delay(tickInterval, stoppingToken);
        }
        _moveLog.Writer.Complete();
    }

    private static (int x, int y) ApplyMove(GameSessionState s, int playerId, string dir)
    {
        var (x, y) = s.PlayerPositions.TryGetValue(playerId, out var pos) ? pos : (0, 0);
        var (dx, dy) = dir switch
        {
            "UP" => (0, -1),
            "DOWN" => (0, 1),
            "LEFT" => (-1, 0),
            "RIGHT" => (1, 0),
            _ => (0, 0)
        };
        var nx = x + dx; var ny = y + dy;
        if (IsWalkable(s.Maze, nx, ny))
            s.PlayerPositions[playerId] = (nx, ny);
        return s.PlayerPositions[playerId];
    }

    private static bool IsWalkable(int[,] maze, int x, int y)
    {
        if (x < 0 || y < 0 || x >= maze.GetLength(0) || y >= maze.GetLength(1)) return false;
        return maze[x, y] == 1;
    }

    private static bool CheckWin(GameSessionState s)
    {
        // Example: player reaches bottom-right
        foreach (var (_, pos) in s.PlayerPositions)
            if (pos == (s.Maze.GetLength(0) - 1, s.Maze.GetLength(1) - 1)) return true;
        return false;
    }
}
