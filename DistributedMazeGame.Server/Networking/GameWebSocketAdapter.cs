// Networking/GameWebSocketAdapter.cs
using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading.Channels;
using DistributedMazeGame.Server.Services;
using DistributedMazeGame.Server.GameLogic;

namespace DistributedMazeGame.Server.Networking
{
    public sealed class GameWebSocketAdapter
    {
        private readonly ConcurrentQueue<ClientInput> _inputs;
        private readonly Channel<GameBroadcast> _broadcasts;

        public GameWebSocketAdapter(ConcurrentQueue<ClientInput> inputs, Channel<GameBroadcast> broadcasts)
        {
            _inputs = inputs;
            _broadcasts = broadcasts;
        }

        // One thread per client for I/O; game logic remains on the authoritative worker
        public void StartClient(WebSocket socket, int playerId, CancellationToken ct)
        {
            var thread = new Thread(async _ =>
            {
                var buffer = new byte[4096];
                while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    try
                    {
                        var doc = JsonDocument.Parse(json);
                        var direction = doc.RootElement.GetProperty("direction").GetString()!;
                        var seq = doc.RootElement.TryGetProperty("seq", out var seqProp) ? seqProp.GetInt32() : 0;
                        _inputs.Enqueue(new ClientInput(playerId, direction, seq, DateTime.UtcNow));
                    }
                    catch { /* ignore malformed */ }
                }
            })
            { IsBackground = true };
            thread.Start();

            // Broadcast sender loop (can be shared)
            _ = Task.Run(async () =>
            {
                await foreach (var msg in _broadcasts.Reader.ReadAllAsync(ct))
                {
                    var json = JsonSerializer.Serialize(new { type = msg.Type, payload = msg.Payload });
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                }
            }, ct);
        }
    }
}
