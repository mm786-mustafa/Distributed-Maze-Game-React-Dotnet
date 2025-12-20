// Networking/WebSocketSessionManager.cs
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DistributedMazeGame.Server.Services;

namespace DistributedMazeGame.Server.Networking
{
    public sealed record SessionInfo(string SessionId, int PlayerCount, bool Started, bool Ended);

    public sealed class WebSocketSessionManager
    {
        private readonly ConcurrentDictionary<string, GameSession> _sessions = new();
        private readonly GameAuthoritativeService _authoritative;

        public WebSocketSessionManager(GameAuthoritativeService authoritative)
        {
            _authoritative = authoritative;
        }

        public async Task HandleClientAsync(string sessionId, WebSocket socket, CancellationToken ct)
        {
            var session = _sessions.GetOrAdd(sessionId, id => new GameSession(id, _authoritative));
            await session.AddPlayerAsync(socket, ct);
        }

        public IReadOnlyList<SessionInfo> GetSessionsSnapshot()
        {
            var list = new List<SessionInfo>();
            foreach (var kv in _sessions)
            {
                var s = kv.Value;
                list.Add(new SessionInfo(kv.Key, s.PlayerCount, s.Started, s.Ended));
            }
            return list;
        }
    }

    internal sealed class GameSession
    {
        private readonly string _sessionId;
        private readonly GameAuthoritativeService _authoritative;
        private readonly object _lock = new();

        private readonly List<PlayerConn> _players = new(2); // exactly two
        private bool _started;
        private bool _ended;

        public GameSession(string sessionId, GameAuthoritativeService authoritative)
        {
            _sessionId = sessionId;
            _authoritative = authoritative;
        }

        public int PlayerCount { get { lock (_lock) { return _players.Count; } } }
        public bool Started { get { lock (_lock) { return _started; } } }
        public bool Ended { get { lock (_lock) { return _ended; } } }

        public async Task AddPlayerAsync(WebSocket socket, CancellationToken ct)
        {
            PlayerConn? playerConn = null;

            lock (_lock)
            {
                if (_players.Count >= 2)
                {
                    // Reject extra connections
                    // Send a short message then close
                    _ = SendAsync(socket, new { type = "ERROR", message = "Session is full" }, ct);
                    _ = socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Session full", ct);
                    return;
                }

                var playerId = (_players.Count == 0) ? 1 : 2;
                playerConn = new PlayerConn(playerId, socket);
                _players.Add(playerConn);
            }

            // Confirm join
            await SendAsync(socket, new { type = "ASSIGNED", playerId = playerConn!.PlayerId, sessionId = _sessionId }, ct);

            // Start session when both connected
            if (_players.Count == 2 && !_started)
            {
                lock (_lock) { _started = true; }

                // Initialize authoritative game state for this session (opposite ends, place flag)
                await _authoritative.InitializeAsync(_sessionId, _players[0].PlayerId, _players[1].PlayerId, ct);

                // Broadcast INIT state
                var initPayload = await _authoritative.GetStateAsync(_sessionId, ct);
                await BroadcastAsync(new { type = "INIT", payload = initPayload }, ct);
            }

            // Begin receive loop for this player
            await ReceiveLoopAsync(playerConn!, ct);
        }

        private async Task ReceiveLoopAsync(PlayerConn player, CancellationToken ct)
        {
            var buffer = new byte[4096];

            try
            {
                while (!ct.IsCancellationRequested && player.Socket.State == WebSocketState.Open)
                {
                    var result = await player.Socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await HandleMessageAsync(player.PlayerId, msg, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // graceful shutdown
            }
            catch (WebSocketException)
            {
                // network error; treat as disconnect
            }
            catch (Exception ex)
            {
                // unexpected error, log in real app
                await SendSafe(player.Socket, new { type = "ERROR", message = "Internal error" }, ct);
            }
            finally
            {
                await HandleDisconnectAsync(player, ct);
            }
        }

        private async Task HandleMessageAsync(int playerId, string raw, CancellationToken ct)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var type = doc.RootElement.GetProperty("type").GetString();

                if (type == "JOIN")
                {
                    // Already handled on connect; ignore duplicates
                    return;
                }

                if (!_started || _ended) return;

                // Map MOVE_* to direction
                string? dir = type switch
                {
                    "MOVE_UP" => "UP",
                    "MOVE_DOWN" => "DOWN",
                    "MOVE_LEFT" => "LEFT",
                    "MOVE_RIGHT" => "RIGHT",
                    _ => null
                };
                if (dir is null) return;

                var result = await _authoritative.ApplyMoveAsync(_sessionId, playerId, dir, ct);

                // Broadcast state or rejection
                if (result.Status == "Rejected")
                {
                    var reject = new { type = "REJECT", payload = new { playerId, direction = dir } };
                    await BroadcastAsync(reject, ct);
                    return;
                }

                var state = await _authoritative.GetStateAsync(_sessionId, ct);
                await BroadcastAsync(new { type = "STATE", payload = state }, ct);

                // End game if completed
                if (result.Completed && !_ended)
                {
                    lock (_lock) { _ended = true; }
                    var endPayload = new { sessionId = _sessionId, winnerPlayerId = result.WinnerPlayerId };
                    await BroadcastAsync(new { type = "END", payload = endPayload }, ct);

                    // Close both sockets gracefully
                    foreach (var p in _players.ToArray())
                    {
                        await CloseSafe(p.Socket, WebSocketCloseStatus.NormalClosure, "Game ended", ct);
                    }
                }
            }
            catch (JsonException)
            {
                // malformed message; ignore or notify
            }
            catch (Exception)
            {
                // prevent crash; optionally notify clients
            }
        }

        private async Task HandleDisconnectAsync(PlayerConn player, CancellationToken ct)
        {
            // Remove player
            lock (_lock)
            {
                _players.RemoveAll(p => p.PlayerId == player.PlayerId);
            }

            // Notify other player
            await BroadcastAsync(new { type = "DISCONNECT", payload = new { playerId = player.PlayerId } }, ct);

            // End session if any player leaves mid-game
            if (_started && !_ended)
            {
                lock (_lock) { _ended = true; }
                await BroadcastAsync(new { type = "END", payload = new { sessionId = _sessionId, reason = "PlayerDisconnected" } }, ct);
                foreach (var p in _players.ToArray())
                {
                    await CloseSafe(p.Socket, WebSocketCloseStatus.NormalClosure, "Session terminated", ct);
                }
            }
        }

        private async Task BroadcastAsync(object message, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);

            var targets = Array.Empty<PlayerConn>();
            lock (_lock)
            {
                targets = _players.ToArray();
            }

            var tasks = targets.Select(p => SendSafe(p.Socket, bytes, ct));
            await Task.WhenAll(tasks);
        }

        private static Task SendAsync(WebSocket socket, object message, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            return socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }

        private static async Task SendSafe(WebSocket socket, object message, CancellationToken ct)
        {
            try { await SendAsync(socket, message, ct); }
            catch { /* ignore send errors */ }
        }

        private static async Task SendSafe(WebSocket socket, byte[] bytes, CancellationToken ct)
        {
            try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
            catch { /* ignore send errors */ }
        }

        private static async Task CloseSafe(WebSocket socket, WebSocketCloseStatus status, string desc, CancellationToken ct)
        {
            try { await socket.CloseAsync(status, desc, ct); } catch { /* ignore */ }
        }

        private sealed record PlayerConn(int PlayerId, WebSocket Socket);
    }
}
