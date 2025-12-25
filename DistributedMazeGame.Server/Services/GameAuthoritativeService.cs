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
// 6. DAILY LEADERBOARD - Pre-aggregated statistics for efficient querying
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
        private const int TOTAL_FLAGS = 4;        // Total flags to capture before game ends
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
            public ConcurrentDictionary<int, string> PlayerNames = new();     // Player names (thread-safe)
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
        /// Set or update a player's display name.
        /// Thread-safe operation using ConcurrentDictionary.
        /// Also persists the name to the database.
        /// </summary>
        public async Task SetPlayerNameAsync(string sessionId, int playerId, string name)
        {
            if (_states.TryGetValue(sessionId, out var s))
            {
                s.PlayerNames[playerId] = name;
                
                // Also update in database
                try
                {
                    await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
                    var player = await db.Players.FirstOrDefaultAsync(p => p.PlayerId == playerId, CancellationToken.None);
                    if (player != null)
                    {
                        player.Name = name;
                        await db.SaveChangesAsync(CancellationToken.None);
                        Console.WriteLine($"[DB] Updated player {playerId} name to '{name}'");
                    }
                    else
                    {
                        // Player doesn't exist yet, create them
                        await db.Players.AddAsync(new Player
                        {
                            PlayerId = playerId,
                            Name = name,
                            ConnectedAt = DateTime.UtcNow
                        }, CancellationToken.None);
                        await db.SaveChangesAsync(CancellationToken.None);
                        Console.WriteLine($"[DB] Created player {playerId} with name '{name}'");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB] Error updating player name: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Get a player's display name.
        /// </summary>
        public string GetPlayerName(string sessionId, int playerId)
        {
            if (_states.TryGetValue(sessionId, out var s) && 
                s.PlayerNames.TryGetValue(playerId, out var name))
            {
                return name;
            }
            return $"Player {playerId}";
        }

        /// <summary>
        /// Initialize a new game session with the given players.
        /// PDC PATTERN: Session initialization with distributed state setup
        /// </summary>
        public async Task InitializeAsync(string sessionId, int[] playerIds, Dictionary<int, string>? playerNames, CancellationToken ct)
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
                var pid = playerIds[i];
                s.Positions[pid] = spawnPoints[i];
                s.Scores[pid] = 0;
                
                // Set player name (from provided names or default)
                var name = playerNames?.GetValueOrDefault(pid) ?? $"Player {pid}";
                s.PlayerNames[pid] = name;
            }

            // Spawn initial flag at a random walkable position
            s.Flag = SpawnFlagPosition(s.Maze, s.Positions.Values.ToList());
            s.FlagsCaptured = 0;
            s.Completed = false;
            s.StartTime = DateTime.UtcNow;

            // SYNCHRONOUS PERSISTENCE: Ensure session exists before gameplay
            // Guarantees s.DbSessionId is set for subsequent persistence
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
                var session = new GameSession
                {
                    StartTime = DateTime.UtcNow,
                    Status = "Active",
                    PlayerCount = playerIds.Length
                };
                await db.GameSessions.AddAsync(session, CancellationToken.None);
                await db.SaveChangesAsync(CancellationToken.None);
                s.DbSessionId = session.SessionId;

                // Ensure all player records exist (for foreign key constraints)
                foreach (var pid in playerIds)
                {
                    var playerName = s.PlayerNames.GetValueOrDefault(pid, $"Player {pid}");
                    var existingPlayer = await db.Players.FirstOrDefaultAsync(x => x.PlayerId == pid, CancellationToken.None);
                    if (existingPlayer == null)
                    {
                        await db.Players.AddAsync(new Player
                        {
                            PlayerId = pid,
                            Name = playerName,
                            ConnectedAt = DateTime.UtcNow
                        }, CancellationToken.None);
                    }
                    else
                    {
                        // Update name if changed
                        existingPlayer.Name = playerName;
                    }
                }
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Init persistence error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the complete current game state for synchronization.
        /// PDC PATTERN: State snapshot for client synchronization
        /// </summary>
        public Task<object> GetStateAsync(string sessionId, CancellationToken ct)
        {
            if (!_states.TryGetValue(sessionId, out var s))
                return Task.FromResult<object>(new { error = "Session not found" });

            // Build player array with positions, scores, and names
            var players = s.Positions.Select(kv => new 
            { 
                id = kv.Key, 
                x = kv.Value.x, 
                y = kv.Value.y,
                score = s.Scores.GetValueOrDefault(kv.Key, 0),
                name = s.PlayerNames.GetValueOrDefault(kv.Key, $"Player {kv.Key}")
            }).ToArray();

            // Build leaderboard (sorted by score descending)
            var leaderboard = s.Scores
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Select((kv, rank) => new 
                { 
                    rank = rank + 1, 
                    playerId = kv.Key, 
                    score = kv.Value,
                    name = s.PlayerNames.GetValueOrDefault(kv.Key, $"Player {kv.Key}")
                })
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
                // Use a non-cancelable token to ensure persistence completes even if the WebSocket request ends
                _ = Task.Run(async () => await PersistGameResultsAsync(s, CancellationToken.None), CancellationToken.None);
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
                    name = s.PlayerNames.GetValueOrDefault(kv.Key, $"Player {kv.Key}"),
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
                winnerName = winner?.name,
                winnerScore = winner?.score,
                totalFlags = TOTAL_FLAGS,
                duration
            });
        }

        /// <summary>
        /// Get today's top winners (daily leaderboard).
        /// PDC PATTERN: Pre-aggregated query for efficient distributed access
        /// </summary>
        public async Task<object> GetDailyLeaderboardAsync(CancellationToken ct)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                
                var dailyLeaderboard = await db.DailyWins
                    .Where(d => d.Date == today)
                    .OrderByDescending(d => d.WinCount)
                    .ThenByDescending(d => d.TotalFlagsCaptured)
                    .Take(10)
                    .Select(d => new
                    {
                        playerId = d.PlayerId,
                        playerName = d.Player.Name,
                        wins = d.WinCount,
                        totalFlags = d.TotalFlagsCaptured,
                        gamesPlayed = d.GamesPlayed
                    })
                    .ToListAsync(ct);

                return new
                {
                    date = today.ToString("yyyy-MM-dd"),
                    leaderboard = dailyLeaderboard
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Daily leaderboard error: {ex.Message}");
                return new { date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), leaderboard = Array.Empty<object>() };
            }
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
        /// PDC PATTERN: Atomic transaction for distributed data consistency
        /// 
        /// This method:
        /// 1. Updates the game session status
        /// 2. Saves individual player scores (PlayerScore table)
        /// 3. Updates the daily win aggregates (DailyWin table)
        /// 4. Creates the result record with winner info
        /// </summary>
        private async Task PersistGameResultsAsync(State s, CancellationToken ct)
        {
            Console.WriteLine($"[DB] PersistGameResultsAsync started for session {s.DbSessionId}");
            
            if (s.DbSessionId <= 0)
            {
                Console.WriteLine($"[DB] PersistGameResultsAsync aborted: DbSessionId is {s.DbSessionId}");
                return;
            }
            
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                
                // Use execution strategy to handle retries with transactions
                // This is required when using MySqlRetryingExecutionStrategy
                var strategy = db.Database.CreateExecutionStrategy();
                
                await strategy.ExecuteAsync(async () =>
                {
                    // Use a transaction to ensure atomicity
                    // PDC CONCEPT: ACID transaction for distributed consistency
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    
                    try
                    {
                        var session = await db.GameSessions.FirstOrDefaultAsync(x => x.SessionId == s.DbSessionId, ct);
                        if (session == null)
                        {
                            Console.WriteLine($"[DB] PersistGameResultsAsync aborted: Session {s.DbSessionId} not found");
                            return;
                        }

                        var endTime = DateTime.UtcNow;
                        session.EndTime = endTime;
                        session.Status = "Completed";
                        session.TotalFlagsCaptured = s.FlagsCaptured;
                        
                        Console.WriteLine($"[DB] Updating session {s.DbSessionId} to Completed");

                        // Get sorted scores to determine rankings
                        var sortedScores = s.Scores
                            .OrderByDescending(kv => kv.Value)
                            .ThenBy(kv => kv.Key)
                            .ToList();
                            
                        Console.WriteLine($"[DB] Processing {sortedScores.Count} player scores");

                        var highestScore = sortedScores.FirstOrDefault().Value;
                        var today = DateOnly.FromDateTime(DateTime.UtcNow);

                        // Save each player's score
                        int rank = 0;
                        foreach (var (playerId, score) in sortedScores)
                        {
                            rank++;
                            var isWinner = score == highestScore && rank == 1;
                            var playerName = s.PlayerNames.GetValueOrDefault(playerId, $"Player {playerId}");
                            
                            Console.WriteLine($"[DB] Processing player {playerId} ({playerName}): score={score}, rank={rank}, isWinner={isWinner}");

                            // Update player name in Players table
                            var playerEntity = await db.Players.FirstOrDefaultAsync(p => p.PlayerId == playerId, ct);
                            if (playerEntity != null)
                            {
                                playerEntity.Name = playerName;
                                Console.WriteLine($"[DB] Updated player {playerId} name to '{playerName}'");
                            }

                            // Create PlayerScore record
                            var playerScore = new PlayerScore
                            {
                                SessionId = s.DbSessionId,
                                PlayerId = playerId,
                                PlayerName = playerName,
                                FlagsCaptured = score,
                                IsWinner = isWinner,
                                FinalRank = rank,
                                RecordedAt = endTime
                            };
                            await db.PlayerScores.AddAsync(playerScore, ct);
                            Console.WriteLine($"[DB] Added PlayerScore for player {playerId}");

                            // Update daily win aggregate
                            // PDC PATTERN: Upsert with atomic increment
                            var dailyWin = await db.DailyWins
                                .FirstOrDefaultAsync(d => d.PlayerId == playerId && d.Date == today, ct);

                            if (dailyWin == null)
                            {
                                dailyWin = new DailyWin
                                {
                                    PlayerId = playerId,
                                    Date = today,
                                    WinCount = isWinner ? 1 : 0,
                                    TotalFlagsCaptured = score,
                                    GamesPlayed = 1,
                                    LastUpdated = DateTime.UtcNow
                                };
                                await db.DailyWins.AddAsync(dailyWin, ct);
                                Console.WriteLine($"[DB] Created DailyWin for player {playerId} on {today}");
                            }
                            else
                            {
                                if (isWinner) dailyWin.WinCount++;
                                dailyWin.TotalFlagsCaptured += score;
                                dailyWin.GamesPlayed++;
                                dailyWin.LastUpdated = DateTime.UtcNow;
                                Console.WriteLine($"[DB] Updated DailyWin for player {playerId}: wins={dailyWin.WinCount}, flags={dailyWin.TotalFlagsCaptured}, games={dailyWin.GamesPlayed}");
                            }
                        }

                        // Get winner for Result table
                        var winner = sortedScores.FirstOrDefault();
                        var winnerName = s.PlayerNames.GetValueOrDefault(winner.Key, $"Player {winner.Key}");
                        
                        var result = new Result
                        {
                            SessionId = session.SessionId,
                            WinnerPlayerId = winner.Key,
                            Duration = (int)(session.EndTime.GetValueOrDefault() - session.StartTime).TotalSeconds
                        };
                        await db.Results.AddAsync(result, ct);
                        Console.WriteLine($"[DB] Added Result: Winner={winnerName} (Player {winner.Key}), Duration={result.Duration}s");

                        await db.SaveChangesAsync(ct);
                        Console.WriteLine($"[DB] SaveChangesAsync completed");
                        
                        await transaction.CommitAsync(ct);
                        Console.WriteLine($"[DB] Transaction committed successfully");
                        
                        Console.WriteLine($"[DB] Game {s.DbSessionId} results persisted successfully. Winner: {winnerName} (Player {winner.Key})");
                    }
                    catch (Exception innerEx)
                    {
                        Console.WriteLine($"[DB] Transaction error: {innerEx.Message}");
                        Console.WriteLine($"[DB] Stack trace: {innerEx.StackTrace}");
                        await transaction.RollbackAsync(ct);
                        throw;
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Result persistence error: {ex.Message}");
                Console.WriteLine($"[DB] Full exception: {ex}");
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
