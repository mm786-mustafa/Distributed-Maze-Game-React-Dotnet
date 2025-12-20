// Services/MoveLogger.cs
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using DistributedMazeGame.Server.Data;
using DistributedMazeGame.Server.Data.Entities;
using System.Threading.Channels;

public class MoveLogger : BackgroundService
{
    private readonly Channel<AcceptedMove> _channel;
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

    public MoveLogger(Channel<AcceptedMove> channel, IDbContextFactory<ApplicationDbContext> contextFactory)
    {
        _channel = channel;
        _contextFactory = contextFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Dedicated consumer loop; DB context per batch to avoid cross-thread use
        var batch = new List<AcceptedMove>(capacity: 256);
        while (await _channel.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_channel.Reader.TryRead(out var move))
            {
                batch.Add(move);
                if (batch.Count >= 256) { await FlushBatchAsync(batch, stoppingToken); batch.Clear(); }
            }
            if (batch.Count > 0) { await FlushBatchAsync(batch, stoppingToken); batch.Clear(); }
        }
    }

    private async Task FlushBatchAsync(List<AcceptedMove> batch, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        // Bulk insert via AddRangeAsync
        var entities = batch.Select(m => new Move
        {
            SessionId = m.SessionId,
            PlayerId = m.PlayerId,
            Direction = m.Direction,
            Timestamp = m.ServerTime
        }).ToList();

        await db.Moves.AddRangeAsync(entities, ct);
        await db.SaveChangesAsync(ct);
    }
}
