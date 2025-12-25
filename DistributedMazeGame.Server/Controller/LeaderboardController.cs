// =============================================================================
// Controller/LeaderboardController.cs
// =============================================================================
// LEADERBOARD REST API - Provides daily and historical leaderboard data
// 
// PDC CONCEPTS DEMONSTRATED:
// 1. REST API for distributed client access
// 2. Efficient database queries with proper indexing
// 3. Caching considerations for high-traffic endpoints
// =============================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DistributedMazeGame.Server.Data;
using DistributedMazeGame.Server.Services;

namespace DistributedMazeGame.Server.Controller
{
    /// <summary>
    /// REST API controller for leaderboard operations.
    /// Provides endpoints for daily winners and historical stats.
    /// 
    /// PDC PATTERN: Read-optimized API for distributed clients
    /// Multiple clients can query this endpoint simultaneously without
    /// affecting game performance.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public sealed class LeaderboardController : ControllerBase
    {
        private readonly GameAuthoritativeService _gameService;
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

        public LeaderboardController(
            GameAuthoritativeService gameService,
            IDbContextFactory<ApplicationDbContext> dbFactory)
        {
            _gameService = gameService;
            _dbFactory = dbFactory;
        }

        /// <summary>
        /// GET /api/leaderboard/daily
        /// Returns today's top winners.
        /// </summary>
        [HttpGet("daily")]
        public async Task<IActionResult> GetDailyLeaderboard(CancellationToken ct)
        {
            var result = await _gameService.GetDailyLeaderboardAsync(ct);
            return Ok(result);
        }

        /// <summary>
        /// GET /api/leaderboard/daily/{date}
        /// Returns leaderboard for a specific date (format: yyyy-MM-dd).
        /// </summary>
        [HttpGet("daily/{date}")]
        public async Task<IActionResult> GetLeaderboardByDate(string date, CancellationToken ct)
        {
            if (!DateOnly.TryParse(date, out var parsedDate))
            {
                return BadRequest(new { error = "Invalid date format. Use yyyy-MM-dd." });
            }

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                
                var leaderboard = await db.DailyWins
                    .Where(d => d.Date == parsedDate)
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

                return Ok(new
                {
                    date = parsedDate.ToString("yyyy-MM-dd"),
                    leaderboard
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Leaderboard error: {ex.Message}");
                return StatusCode(500, new { error = "Failed to retrieve leaderboard" });
            }
        }

        /// <summary>
        /// GET /api/leaderboard/alltime
        /// Returns all-time top winners (aggregated across all dates).
        /// </summary>
        [HttpGet("alltime")]
        public async Task<IActionResult> GetAllTimeLeaderboard(CancellationToken ct)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                
                // Aggregate wins across all dates
                var allTimeLeaderboard = await db.DailyWins
                    .GroupBy(d => d.PlayerId)
                    .Select(g => new
                    {
                        playerId = g.Key,
                        totalWins = g.Sum(d => d.WinCount),
                        totalFlags = g.Sum(d => d.TotalFlagsCaptured),
                        totalGames = g.Sum(d => d.GamesPlayed)
                    })
                    .OrderByDescending(x => x.totalWins)
                    .ThenByDescending(x => x.totalFlags)
                    .Take(20)
                    .ToListAsync(ct);

                // Get player names
                var playerIds = allTimeLeaderboard.Select(x => x.playerId).ToList();
                var playerNames = await db.Players
                    .Where(p => playerIds.Contains(p.PlayerId))
                    .ToDictionaryAsync(p => p.PlayerId, p => p.Name, ct);

                var result = allTimeLeaderboard.Select(x => new
                {
                    x.playerId,
                    playerName = playerNames.GetValueOrDefault(x.playerId, $"Player {x.playerId}"),
                    x.totalWins,
                    x.totalFlags,
                    x.totalGames,
                    winRate = x.totalGames > 0 ? Math.Round((double)x.totalWins / x.totalGames * 100, 1) : 0
                });

                return Ok(new { leaderboard = result });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] All-time leaderboard error: {ex.Message}");
                return StatusCode(500, new { error = "Failed to retrieve all-time leaderboard" });
            }
        }

        /// <summary>
        /// GET /api/leaderboard/player/{playerId}
        /// Returns a specific player's statistics.
        /// </summary>
        [HttpGet("player/{playerId}")]
        public async Task<IActionResult> GetPlayerStats(int playerId, CancellationToken ct)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                
                var player = await db.Players.FirstOrDefaultAsync(p => p.PlayerId == playerId, ct);
                if (player == null)
                {
                    return NotFound(new { error = "Player not found" });
                }

                // Get aggregated stats
                var stats = await db.DailyWins
                    .Where(d => d.PlayerId == playerId)
                    .GroupBy(d => d.PlayerId)
                    .Select(g => new
                    {
                        totalWins = g.Sum(d => d.WinCount),
                        totalFlags = g.Sum(d => d.TotalFlagsCaptured),
                        totalGames = g.Sum(d => d.GamesPlayed),
                        daysPlayed = g.Count()
                    })
                    .FirstOrDefaultAsync(ct);

                // Get recent games
                var recentGames = await db.PlayerScores
                    .Where(ps => ps.PlayerId == playerId)
                    .OrderByDescending(ps => ps.RecordedAt)
                    .Take(10)
                    .Select(ps => new
                    {
                        sessionId = ps.SessionId,
                        flagsCaptured = ps.FlagsCaptured,
                        rank = ps.FinalRank,
                        isWinner = ps.IsWinner,
                        playedAt = ps.RecordedAt
                    })
                    .ToListAsync(ct);

                return Ok(new
                {
                    playerId,
                    playerName = player.Name,
                    memberSince = player.ConnectedAt,
                    totalWins = stats?.totalWins ?? 0,
                    totalFlags = stats?.totalFlags ?? 0,
                    totalGames = stats?.totalGames ?? 0,
                    daysPlayed = stats?.daysPlayed ?? 0,
                    winRate = stats != null && stats.totalGames > 0 
                        ? Math.Round((double)stats.totalWins / stats.totalGames * 100, 1) 
                        : 0,
                    recentGames
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Player stats error: {ex.Message}");
                return StatusCode(500, new { error = "Failed to retrieve player stats" });
            }
        }

        /// <summary>
        /// GET /api/leaderboard/recent
        /// Returns recent game results.
        /// </summary>
        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentGames(CancellationToken ct)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                
                var recentGames = await db.GameSessions
                    .Where(s => s.Status == "Completed")
                    .OrderByDescending(s => s.EndTime)
                    .Take(20)
                    .Select(s => new
                    {
                        sessionId = s.SessionId,
                        startTime = s.StartTime,
                        endTime = s.EndTime,
                        duration = s.Result != null ? s.Result.Duration : 0,
                        playerCount = s.PlayerCount,
                        totalFlags = s.TotalFlagsCaptured,
                        winnerId = s.Result != null ? s.Result.WinnerPlayerId : (int?)null,
                        winnerName = s.Result != null ? s.Result.Winner.Name : null
                    })
                    .ToListAsync(ct);

                return Ok(new { games = recentGames });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] Recent games error: {ex.Message}");
                return StatusCode(500, new { error = "Failed to retrieve recent games" });
            }
        }
    }
}
