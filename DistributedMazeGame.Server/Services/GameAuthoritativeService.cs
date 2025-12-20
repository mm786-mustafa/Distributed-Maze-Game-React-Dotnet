// Services/GameAuthoritativeService.cs
using System.Collections.Concurrent;
using DistributedMazeGame.Server.Data;
using DistributedMazeGame.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DistributedMazeGame.Server.Services
{
    public sealed class GameAuthoritativeService
    {
        private sealed class State
        {
            public int[,] Maze = default!;
            public (int x, int y) Flag;
            public ConcurrentDictionary<int, (int x, int y)> Positions = new();
            public bool Completed;
            public int DbSessionId;
        }

        private readonly ConcurrentDictionary<string, State> _states = new();
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

        public GameAuthoritativeService(IDbContextFactory<ApplicationDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task InitializeAsync(string sessionId, int p1, int p2, CancellationToken ct)
        {
            var s = _states.GetOrAdd(sessionId, _ => new State());

            // For brevity, use a simple open grid; in production, load from MazeGenerator
            var n = 21; var m = 21;
            s.Maze = new int[n, m];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < m; j++)
                    s.Maze[i, j] = 1; // walkable

            s.Positions[p1] = (0, 0);                 // top-left
            s.Positions[p2] = (n - 1, m - 1);         // bottom-right
            s.Flag = (n / 2, m / 2);                  // center
            s.Completed = false;

            // Persist session start and store DbSessionId (non-blocking)
            _ = Task.Run(async () =>
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                var session = new GameSession
                {
                    StartTime = DateTime.UtcNow,
                    Status = "Active"
                };
                await db.GameSessions.AddAsync(session, ct);
                await db.SaveChangesAsync(ct);
                s.DbSessionId = session.SessionId;

                // Ensure Players exist to satisfy FK for Moves
                var p1Entity = await db.Players.FirstOrDefaultAsync(x => x.PlayerId == p1, ct);
                if (p1Entity == null)
                {
                    await db.Players.AddAsync(new Player
                    {
                        PlayerId = p1,
                        Name = $"Player {p1}",
                        ConnectedAt = DateTime.UtcNow
                    }, ct);
                }

                var p2Entity = await db.Players.FirstOrDefaultAsync(x => x.PlayerId == p2, ct);
                if (p2Entity == null)
                {
                    await db.Players.AddAsync(new Player
                    {
                        PlayerId = p2,
                        Name = $"Player {p2}",
                        ConnectedAt = DateTime.UtcNow
                    }, ct);
                }

                await db.SaveChangesAsync(ct);
            }, ct);
        }

        public Task<object> GetStateAsync(string sessionId, CancellationToken ct)
        {
            var s = _states[sessionId];
            var players = s.Positions.Select(kv => new { id = kv.Key, x = kv.Value.x, y = kv.Value.y }).ToArray();
            var payload = new
            {
                sessionId,
                flag = new { x = s.Flag.x, y = s.Flag.y },
                players
            };
            return Task.FromResult<object>(payload);
        }

        public async Task<(string Status, bool Completed, int? WinnerPlayerId)> ApplyMoveAsync(
            string sessionId, int playerId, string direction, CancellationToken ct)
        {
            var s = _states[sessionId];
            if (s.Completed || !s.Positions.ContainsKey(playerId))
                return ("Rejected", s.Completed, null);

            var (x, y) = s.Positions[playerId];
            var (dx, dy) = direction switch
            {
                "UP" => (0, -1),
                "DOWN" => (0, 1),
                "LEFT" => (-1, 0),
                "RIGHT" => (1, 0),
                _ => (0, 0)
            };

            var nx = x + dx; var ny = y + dy;
            if (!IsWalkable(s.Maze, nx, ny))
                return ("Rejected", s.Completed, null);

            s.Positions[playerId] = (nx, ny);

            if (nx == s.Flag.x && ny == s.Flag.y)
            {
                s.Completed = true;

                // Persist result asynchronously
                _ = Task.Run(async () =>
                {
                    if (s.DbSessionId <= 0) return; // not persisted yet
                    await using var db = await _dbFactory.CreateDbContextAsync(ct);
                    var session = await db.GameSessions.FirstOrDefaultAsync(x => x.SessionId == s.DbSessionId, ct);
                    if (session == null) return;

                    session.EndTime = DateTime.UtcNow;
                    session.Status = "Completed";
                    await db.Results.AddAsync(new Result
                    {
                        SessionId = session.SessionId,
                        WinnerPlayerId = playerId,
                        Duration = (int)(session.EndTime.GetValueOrDefault() - session.StartTime).TotalSeconds
                    }, ct);
                    await db.SaveChangesAsync(ct);
                }, ct);

                return ("Accepted", true, playerId);
            }

            // Persist move asynchronously (non-blocking)
            _ = Task.Run(async () =>
            {
                if (s.DbSessionId <= 0) return; // session not stored yet
                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                // Ensure Player exists (handles reconnect edge-cases)
                var player = await db.Players.FirstOrDefaultAsync(x => x.PlayerId == playerId, ct);
                if (player == null)
                {
                    await db.Players.AddAsync(new Player
                    {
                        PlayerId = playerId,
                        Name = $"Player {playerId}",
                        ConnectedAt = DateTime.UtcNow
                    }, ct);
                    await db.SaveChangesAsync(ct);
                }

                await db.Moves.AddAsync(new Move
                {
                    PlayerId = playerId,
                    SessionId = s.DbSessionId,
                    Direction = direction,
                    Timestamp = DateTime.UtcNow
                }, ct);
                await db.SaveChangesAsync(ct);
            }, ct);

            return ("Accepted", false, null);
        }

        private static bool IsWalkable(int[,] maze, int x, int y)
        {
            if (x < 0 || y < 0 || x >= maze.GetLength(0) || y >= maze.GetLength(1)) return false;
            return maze[x, y] == 1;
        }
    }
}
