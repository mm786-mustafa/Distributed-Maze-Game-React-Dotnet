// Networking/WebSocketHandler.cs
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

public class WebSocketHandler
{
    private readonly ConcurrentQueue<PlayerInput> _inputQueue;

    public WebSocketHandler(ConcurrentQueue<PlayerInput> inputQueue)
    {
        _inputQueue = inputQueue;
    }

    public async Task HandleAsync(WebSocket socket, int playerId, CancellationToken ct)
    {
        // Dedicated thread for this client I/O
        var thread = new Thread(() => ClientLoop(socket, playerId, ct)) { IsBackground = true };
        thread.Start();
    }

    private async void ClientLoop(WebSocket socket, int playerId, CancellationToken ct)
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
                var seq = doc.RootElement.GetProperty("seq").GetInt32();

                _inputQueue.Enqueue(new PlayerInput(playerId, seq, direction, DateTime.UtcNow));
            }
            catch { /* sanitize/ignore malformed */ }
        }
    }
}
