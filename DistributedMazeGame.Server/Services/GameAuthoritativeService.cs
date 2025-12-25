// =============================================================================
// Services/GameAuthoritativeService.cs
// =============================================================================
// AUTHORITATIVE GAME SERVER - Core of the Distributed System
// 
// PDC CONCEPTS DEMONSTRATED:
// 1. CENTRALIZED AUTHORITY - Server is the single source of truth for game state
// 2. RACE CONDITION PREVENTION - Uses locks to prevent simultaneous flag captures
// 3. CONCURRENT DATA STRUCTURES - ConcurrentDictionary for thread-safe state
// 4. DISTRIBUTED STATE SYNCHRONIZATION - All clients receive same authoritative state
// 5. ASYNCHRONOUS PERSISTENCE - Non-blocking database writes via Task.Run
// =============================================================================

using System.Collections.Concurrent;
using DistributedMazeGame.Server.Data;
using DistributedMazeGame.Server.Data.Entities;
using DistributedMazeGame.Server.GameLogic;
using Microsoft.EntityFrameworkCore;

namespace DistributedMazeGame.Server.Services
{
    /// <summary>
    /// The authoritative game service manages all game state on the server.
    /// This is a KEY PDC PATTERN: Centralized Authority Model
    /// - Prevents cheating by validating all moves server-side
    /// - Eliminates conflicts by having a single source of truth
    /// - Ensures consistency across all distributed clients
    /// </summary>
    public sealed class GameAuthoritativeService
    {
        // Game configuration constants
        private const int TOTAL_FLAGS = 10;        // Total flags to capture before game ends
        private const int MAZE_SIZE = 21;          // Grid dimension (odd for proper maze generation)
        private const int MAX_PLAYERS = 4;         // Support up to 4 players

        /// <summary>
        /// Internal state for each game session.
        /// Each session runs independently (horizontal scalability pattern).
        /// </summary>
        private sealed class State
        {
            public int[,] Maze = default!;                                    // 2D maze grid: 1=path, 0=wall
            public (int x, int y) Flag;                                       // Current flag position
            public ConcurrentDictionary<int, (int x, int y)> Positions = new(); // Player positions (thread-safe)
            public ConcurrentDictionary<int, int> Scores = new();             // Player scores (thread-safe)
            public int FlagsCaptured;                                         // Number of flags captured so far
            public bool Completed;                                            // Game ended flag
            public int DbSessionId;                                           // Database session ID for persistence
            public DateTime StartTime;                                        // Game start timestamp
            
            // CRITICAL: Lock object for flag capture synchronization
            // This prevents race conditions when multiple players reach the flag simultaneously
            public readonly object FlagLock = new();
        }

        // Thread-safe dictionary holding all active game sessions
        // KEY PDC CONCEPT: Concurrent data structure for multi-threaded access
        private readonly ConcurrentDictionary<string, State> _states = new();
        
        // Factory pattern for database connections (allows parallel DB operations)
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        
        // Thread-safe random number generator for flag spawning
        private static readonly ThreadLocal<Random> _random = 
            new(() => new Random(Guid.NewGuid().GetHashCode()));

        public GameAuthoritativeService(IDbContextFactory<ApplicationDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        /// <summary>
        /// Initialize a new game session with the given players.
        /// PDC PATTERN: Session initialization with distributed state setup
        /// </summary>
        public async Task InitializeAsync(string sessionId, int[] playerIds, CancellationToken ct)
        {
            var s = _states.GetOrAdd(sessionId, _ => new State());
            var n = MAZE_SIZE;

            // Generate a real maze using randomized DFS algorithm
            // This runs synchronously as it's needed before game starts
            var generator = new MazeGenerator(n, n);
            generator.Generate();
            s.Maze = generator.GetMaze();
            
            // Ensure start positions are walkable (corners and edges)
            EnsureWalkable(s.Maze, 0, 0);           // Top-left
            EnsureWalkable(s.Maze, n - 1, n - 1);   // Bottom-right
            EnsureWalkable(s.Maze, 0, n - 1);       // Top-right
            EnsureWalkable(s.Maze, n - 1, 0);       // Bottom-left

            // Assign spawn positions based on player count (distributed across corners)
            var spawnPoints = new[] { (0, 0), (n - 1, n - 1), (0, n - 1), (n - 1, 0) };
            for (int i = 0; i < playerIds.Length && i < MAX_PLAYERS; i++)
            {
                s.Positions[playerIds[i]] = spawnPoints[i];
                s.Scores[playerIds[i]] = 0;
            }

            // Spawn initial flag at a random walkable position
            s.Flag = SpawnFlagPosition(s.Maze, s.Positions.Values.ToList());
            s.FlagsCaptured = 0;
            s.Completed = false;
            s.StartTime = DateTime.UtcNow;

            // ASYNC PERSISTENCE: Non-blocking database write
            // PDC PATTERN: Fire-and-forget for non-critical operations
            _ = Task.Run(async () =>
            {
                try
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

                    // Ensure all player records exist (for foreign key constraints)
                    foreach (var pid in playerIds)
                    {
                        var exists = await db.Players.AnyAsync(x => x.PlayerId == pid, ct);
                        if (!exists)
                        {
                            await db.Players.AddAsync(new Player
                            {
                                PlayerId = pid,
                                Name = $"Player {pid}",
                                ConnectedAt = DateTime.UtcNow
                            }, ct);
                        }
                    }
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB] Init persistence error: {ex.Message}");
                }
            }, ct);
        }

        /// <summary>
        /// Get the complete current game state for synchronization.
        /// PDC PATTERN: State snapshot for client synchronization
        /// </summary>
        public Task<object> GetStateAsync(string sessionId, CancellationToken ct)
        {
            if (!_states.TryGetValue(sessionId, out var s))
                return Task.FromResult<object>(new { error = "Session not found" });

            // Build player array with positions and scores
            var players = s.Positions.Select(kv => new 
            { 
                id = kv.Key, 
                x = kv.Value.x, 
                y = kv.Value.y,
                score = s.Scores.GetValueOrDefault(kv.Key, 0)
            }).ToArray();

            // Build leaderboard (sorted by score descending)
            var leaderboard = s.Scores
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Select((kv, rank) => new { rank = rank + 1, playerId = kv.Key, score = kv.Value })
                .ToArray();

            var payload = new
            {
                sessionId,
                flag = new { x = s.Flag.x, y = s.Flag.y },
                players,
                leaderboard,
                flagsCaptured = s.FlagsCaptured,
                totalFlags = TOTAL_FLAGS,
                maze = ConvertMazeToArray(s.Maze),
                completed = s.Completed
            };
            return Task.FromResult<object>(payload);
        }

        /// <summary>
        /// Get just the maze data (for initial load optimization).
        /// </summary>
        public Task<int[][]?> GetMazeAsync(string sessionId)
        {
            if (!_states.TryGetValue(sessionId, out var s))
                return Task.FromResult<int[][]?>(null);
            return Task.FromResult<int[][]?>(ConvertMazeToArray(s.Maze));
        }

        /// <summary>
        /// Apply a player move and check for flag capture.
        /// 
        /// PDC CRITICAL SECTION: This method handles the most important distributed
        /// systems challenge - preventing race conditions when multiple players
        /// attempt to capture the same flag simultaneously.
        /// 
        /// SOLUTION: Use a lock around the flag capture check to ensure atomicity.
        /// Only one player can capture a flag even if they reach it at the same tick.
        /// </summary>
        public async Task<GameMoveResult> ApplyMoveAsync(
            string sessionId, int playerId, string direction, CancellationToken ct)
        {
            if (!_states.TryGetValue(sessionId, out var s))
                return new GameMoveResult("Rejected", false, null, null);

            if (s.Completed || !s.Positions.ContainsKey(playerId))
                return new GameMoveResult("Rejected", s.Completed, null, null);

            var (x, y) = s.Positions[playerId];
            var (dx, dy) = direction switch
            {
                "UP" => (0, -1),
                "DOWN" => (0, 1),
                "LEFT" => (-1, 0),
                "RIGHT" => (1, 0),
                _ => (0, 0)
            };

            var nx = x + dx;
            var ny = y + dy;

            // COLLISION DETECTION: Check if new position is walkable
            if (!IsWalkable(s.Maze, nx, ny))
                return new GameMoveResult("Rejected", s.Completed, null, null);

            // Update player position (ConcurrentDictionary handles thread-safety)
            s.Positions[playerId] = (nx, ny);

            // =========================================================================
            // CRITICAL SECTION: Flag Capture with Race Condition Prevention
            // =========================================================================
            // PDC CONCEPT: Mutual Exclusion using lock
            // Multiple players might reach the flag in the same server tick.
            // The lock ensures only ONE player captures it (first-come-first-served).
            // =========================================================================
            int? capturedBy = null;
            (int x, int y)? newFlagPos = null;
            bool gameCompleted = false;

            lock (s.FlagLock)
            {
                // Double-check: flag might have been captured by another thread
                if (nx == s.Flag.x && ny == s.Flag.y && !s.Completed)
                {
                    // INCREMENT SCORE - Atomic within lock
                    s.Scores.AddOrUpdate(playerId, 1, (_, old) => old + 1);
                    s.FlagsCaptured++;
                    capturedBy = playerId;

                    // Check if game is complete (all flags captured)
                    if (s.FlagsCaptured >= TOTAL_FLAGS)
                    {
                        s.Completed = true;
                        gameCompleted = true;
                    }
                    else
                    {
                        // Spawn new flag at different location
                        s.Flag = SpawnFlagPosition(s.Maze, s.Positions.Values.ToList());
                        newFlagPos = s.Flag;
                    }
                }
            }
            // =========================================================================
            // END CRITICAL SECTION
            // =========================================================================

            // ASYNC PERSISTENCE: Log move to database (non-blocking)
            _ = Task.Run(async () =>
            {
                if (s.DbSessionId <= 0) return;
                try
                {
                    await using var db = await _dbFactory.CreateDbContextAsync(ct);

                    // Ensure player exists
                    var exists = await db.Players.AnyAsync(p => p.PlayerId == playerId, ct);
                    if (!exists)
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
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB] Move persistence error: {ex.Message}");
                }
            }, ct);

            // If game completed, persist final results
            if (gameCompleted)
            {
                _ = Task.Run(async () => await PersistGameResultsAsync(s, ct), ct);
            }

            return new GameMoveResult("Accepted", gameCompleted, capturedBy, newFlagPos);
        }

        /// <summary>
        /// Get the final leaderboard for a completed game.
        /// </summary>
        public Task<object?> GetLeaderboardAsync(string sessionId)
        {
            if (!_states.TryGetValue(sessionId, out var s))
                return Task.FromResult<object?>(null);

            var leaderboard = s.Scores
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Select((kv, rank) => new 
                { 
                    rank = rank + 1, 
                    playerId = kv.Key, 
                    score = kv.Value,
                    isWinner = rank == 0
                })
                .ToArray();

            var winner = leaderboard.FirstOrDefault();
            var duration = s.Completed 
                ? (int)(DateTime.UtcNow - s.StartTime).TotalSeconds 
                : 0;

            return Task.FromResult<object?>(new
            {
                sessionId,
                completed = s.Completed,
                leaderboard,
                winnerId = winner?.playerId,
                winnerScore = winner?.score,
                totalFlags = TOTAL_FLAGS,
                duration
            });
        }

        /// <summary>
        /// Add a new player to an existing session (for late joiners).
        /// </summary>
        public bool TryAddPlayer(string sessionId, int playerId)
        {
            if (!_states.TryGetValue(sessionId, out var s))
                return false;
            if (s.Positions.Count >= MAX_PLAYERS)
                return false;
            if (s.Completed)
                return false;

            var n = MAZE_SIZE;
            var spawnPoints = new[] { (0, 0), (n - 1, n - 1), (0, n - 1), (n - 1, 0) };
            var usedSpawns = s.Positions.Values.ToHashSet();
            var freeSpawn = spawnPoints.FirstOrDefault(sp => !usedSpawns.Contains(sp));
            
            if (freeSpawn == default) return false;

            s.Positions[playerId] = freeSpawn;
            s.Scores[playerId] = 0;
            return true;
        }

        // =========================================================================
        // HELPER METHODS
        // =========================================================================

        /// <summary>
        /// Persist final game results to database.
        /// </summary>
        private async Task PersistGameResultsAsync(State s, CancellationToken ct)
        {
            if (s.DbSessionId <= 0) return;
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                var session = await db.GameSessions.FirstOrDefaultAsync(x => x.SessionId == s.DbSessionId, ct);
                if (session == null) return;

                session.EndTime = DateTime.UtcNow;
                session.Status = "Completed";

                // Get winner (highest score)
                var winner = s.Scores.OrderByDescending(kv => kv.Value).FirstOrDefault();
                
                await db.Results.AddAsync(new Result
                {
                    SessionId = session.SessionId,
                    WinnerPlayerId = winner.Key,
                    Duration = (int)(session.EndTime.GetValueOrDefault() - session.StartTime).TotalSeconds
                }, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Result persistence error: {ex.Message}");
            }
        }

        /// <summary>
        /// Spawn a flag at a random walkable position, avoiding player positions.
        /// Uses thread-local Random for thread safety.
        /// </summary>
        private static (int x, int y) SpawnFlagPosition(int[,] maze, List<(int x, int y)> avoidPositions)
        {
            var rows = maze.GetLength(0);
            var cols = maze.GetLength(1);
            var walkable = new List<(int x, int y)>();

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    if (maze[y, x] == 1 && !avoidPositions.Contains((x, y)))
                    {
                        walkable.Add((x, y));
                    }
                }
            }

            if (walkable.Count == 0)
                return (cols / 2, rows / 2); // Fallback to center

            return walkable[_random.Value!.Next(walkable.Count)];
        }

        /// <summary>
        /// Ensure a specific cell and its neighbors are walkable (for spawn points).
        /// </summary>
        private static void EnsureWalkable(int[,] maze, int x, int y)
        {
            var rows = maze.GetLength(0);
            var cols = maze.GetLength(1);
            
            if (x >= 0 && x < cols && y >= 0 && y < rows)
                maze[y, x] = 1;
            if (x + 1 < cols && y >= 0 && y < rows)
                maze[y, x + 1] = 1;
            if (x >= 0 && y + 1 < rows)
                maze[y + 1, x] = 1;
        }

        /// <summary>
        /// Check if a position is within bounds and walkable.
        /// </summary>
        private static bool IsWalkable(int[,] maze, int x, int y)
        {
            var rows = maze.GetLength(0);
            var cols = maze.GetLength(1);
            if (x < 0 || y < 0 || x >= cols || y >= rows) return false;
            return maze[y, x] == 1;
        }

        /// <summary>
        /// Convert 2D array to jagged array for JSON serialization.
        /// </summary>
        private static int[][] ConvertMazeToArray(int[,] maze)
        {
            var rows = maze.GetLength(0);
            var cols = maze.GetLength(1);
            var result = new int[rows][];
            for (int r = 0; r < rows; r++)
            {
                result[r] = new int[cols];
                for (int c = 0; c < cols; c++)
                {
                    result[r][c] = maze[r, c];
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Result of applying a move. Used to communicate outcome to WebSocket layer.
    /// Named GameMoveResult to avoid conflict with GameLogic.MoveResult enum.
    /// </summary>
    public record GameMoveResult(
        string Status,           // "Accepted" or "Rejected"
        bool Completed,          // Whether game has ended
        int? CapturedBy,         // Player who captured flag (if any)
        (int x, int y)? NewFlag  // New flag position (if spawned)
    );
}
