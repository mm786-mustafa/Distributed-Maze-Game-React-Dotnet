// Services/GameAuthoritativeWorker.cs
using DistributedMazeGame.Server.Data;
using DistributedMazeGame.Server.Data.Entities;
using DistributedMazeGame.Server.GameLogic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DistributedMazeGame.Server.Services
{
    public record ClientInput(int PlayerId, string Direction, int Seq, DateTime ClientTime);

    public sealed class GameAuthoritativeWorker : BackgroundService
    {
        // Thread-safe ingress and egress
        private readonly ConcurrentQueue<ClientInput> _inputs;
        private readonly Channel<GameBroadcast> _broadcasts;
        private readonly GameStateManager _state;
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

        public GameAuthoritativeWorker(
            ConcurrentQueue<ClientInput> inputs,
            Channel<GameBroadcast> broadcasts,
            GameStateManager state,
            IDbContextFactory<ApplicationDbContext> dbFactory)
        {
            _inputs = inputs;
            _broadcasts = broadcasts;
            _state = state;
            _dbFactory = dbFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var tickInterval = TimeSpan.FromMilliseconds(33); // ~30Hz
            while (!stoppingToken.IsCancellationRequested)
            {
                // Drain inputs on the single authoritative thread
                while (_inputs.TryDequeue(out var input))
                {
                    var result = _state.TryApplyMove(input.PlayerId, input.Direction, out var updated);

                    // Log moves asynchronously via fire-and-forget task; do not block
                    _ = LogMoveAsync(input, result, stoppingToken);

                    if (result == MoveResult.Completed)
                    {
                        // Persist winner and session completion (async, do not block game thread)
                        _ = PersistResultAsync(_state.SessionId, input.PlayerId, stoppingToken);
                    }
                }

                await Task.Delay(tickInterval, stoppingToken);
            }
        }

        // Immediate broadcast consumer to WebSocket layer
        public async Task ConsumeBroadcastsAsync(Func<string, Task> sendAsync, CancellationToken ct)
        {
            await foreach (var msg in _broadcasts.Reader.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(new { type = msg.Type, payload = msg.Payload });
                await sendAsync(json);
            }
        }

        private async Task LogMoveAsync(ClientInput input, MoveResult result, CancellationToken ct)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                await db.Moves.AddAsync(new Move
                {
                    PlayerId = input.PlayerId,
                    SessionId = _state.SessionId,
                    Direction = input.Direction,
                    Timestamp = DateTime.UtcNow
                }, ct);
                await db.SaveChangesAsync(ct);
            }
            catch
            {
                // Consider retry/backoff or a resilient queue in production
            }
        }

        private async Task PersistResultAsync(int sessionId, int winnerPlayerId, CancellationToken ct)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                var session = await db.GameSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
                if (session != null)
                {
                    session.EndTime = DateTime.UtcNow;
                    session.Status = "Completed";
                }

                await db.Results.AddAsync(new Result
                {
                    SessionId = sessionId,
                    WinnerPlayerId = winnerPlayerId,
                    Duration = session != null && session.StartTime != default
                        ? (int)(DateTime.UtcNow - session.StartTime).TotalSeconds
                        : 0
                }, ct);

                await db.SaveChangesAsync(ct);
            }
            catch
            {
                // Handle duplicates via unique constraint on SessionId in Results; add retries if needed
            }
        }
    }
}
