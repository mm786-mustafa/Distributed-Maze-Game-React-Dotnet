// =============================================================================
// Networking/WebSocketSessionManager.cs
// =============================================================================
// WEBSOCKET SESSION MANAGEMENT - Real-Time Communication Layer
// 
// PDC CONCEPTS DEMONSTRATED:
// 1. CONCURRENT CONNECTION HANDLING - Multiple clients per session
// 2. EVENT-DRIVEN ARCHITECTURE - Async message processing
// 3. BROADCAST PATTERN - Efficient multi-client state synchronization
// 4. CONNECTION POOLING - Managing WebSocket lifecycle
// 5. THREAD-SAFE COLLECTIONS - ConcurrentDictionary for session state
// =============================================================================

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DistributedMazeGame.Server.Services;

namespace DistributedMazeGame.Server.Networking
{
    /// <summary>
    /// Provides information about an active game session.
    /// Used by the diagnostics API.
    /// </summary>
    public sealed record SessionInfo(
        string SessionId, 
        int PlayerCount, 
        bool Started, 
        bool Ended,
        string[] PlayerIds
    );

    /// <summary>
    /// Manages all WebSocket game sessions.
    /// 
    /// PDC PATTERN: Session Manager / Connection Pool
    /// - Handles multiple concurrent game sessions
    /// - Routes messages to appropriate game logic
    /// - Manages player connections and disconnections
    /// </summary>
    public sealed class WebSocketSessionManager
    {
        // Thread-safe dictionary of all active sessions
        private readonly ConcurrentDictionary<string, GameSession> _sessions = new();
        
        // Reference to the authoritative game service
        private readonly GameAuthoritativeService _authoritative;

        public WebSocketSessionManager(GameAuthoritativeService authoritative)
        {
            _authoritative = authoritative;
        }

        /// <summary>
        /// Handle a new WebSocket client connection.
        /// PDC PATTERN: Connection routing and session assignment
        /// </summary>
        public async Task HandleClientAsync(string sessionId, WebSocket socket, CancellationToken ct)
        {
            // GetOrAdd is atomic - ensures only one session per ID
            var session = _sessions.GetOrAdd(sessionId, id => new GameSession(id, _authoritative));
            await session.AddPlayerAsync(socket, ct);
        }

        /// <summary>
        /// Get a snapshot of all active sessions (for monitoring/debugging).
        /// </summary>
        public IReadOnlyList<SessionInfo> GetSessionsSnapshot()
        {
            var list = new List<SessionInfo>();
            foreach (var kv in _sessions)
            {
                var s = kv.Value;
                list.Add(new SessionInfo(
                    kv.Key, 
                    s.PlayerCount, 
                    s.Started, 
                    s.Ended,
                    s.GetPlayerIds()
                ));
            }
            return list;
        }

        /// <summary>
        /// Get info about a specific session.
        /// </summary>
        public SessionInfo? GetSession(string sessionId)
        {
            if (!_sessions.TryGetValue(sessionId, out var s))
                return null;
            return new SessionInfo(sessionId, s.PlayerCount, s.Started, s.Ended, s.GetPlayerIds());
        }
    }

    /// <summary>
    /// Represents a single game session with multiple players.
    /// 
    /// PDC CONCEPTS:
    /// - WAITING ROOM PATTERN: Collect players before starting
    /// - BROADCAST: Send state updates to all connected clients
    /// - GRACEFUL DEGRADATION: Handle disconnections mid-game
    /// </summary>
    internal sealed class GameSession
    {
        // Configuration
        private const int MIN_PLAYERS_TO_START = 2;  // Minimum players to start
        private const int MAX_PLAYERS = 4;           // Maximum players allowed

        private readonly string _sessionId;
        private readonly GameAuthoritativeService _authoritative;
        
        // Lock for thread-safe state modifications
        private readonly object _lock = new();

        // Connected players
        private readonly List<PlayerConn> _players = new(MAX_PLAYERS);
        
        // Player ID counter (incremented atomically)
        private int _nextPlayerId = 1;
        
        // Session state
        private bool _started;
        private bool _ended;

        public GameSession(string sessionId, GameAuthoritativeService authoritative)
        {
            _sessionId = sessionId;
            _authoritative = authoritative;
        }

        // Thread-safe property accessors
        public int PlayerCount { get { lock (_lock) { return _players.Count; } } }
        public bool Started { get { lock (_lock) { return _started; } } }
        public bool Ended { get { lock (_lock) { return _ended; } } }
        
        public string[] GetPlayerIds()
        {
            lock (_lock)
            {
                return _players.Select(p => p.PlayerId.ToString()).ToArray();
            }
        }

        /// <summary>
        /// Get the dictionary of player IDs to names.
        /// </summary>
        private Dictionary<int, string> GetPlayerNames()
        {
            lock (_lock)
            {
                return _players.ToDictionary(p => p.PlayerId, p => p.Name);
            }
        }

        /// <summary>
        /// Add a new player to the session.
        /// Implements the WAITING ROOM pattern - game starts when enough players join.
        /// </summary>
        public async Task AddPlayerAsync(WebSocket socket, CancellationToken ct)
        {
            PlayerConn? playerConn = null;

            lock (_lock)
            {
                // Reject if session is full or game has ended
                if (_players.Count >= MAX_PLAYERS || _ended)
                {
                    _ = SendAsync(socket, new { type = "ERROR", message = "Session is full or ended" }, ct);
                    _ = socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Session unavailable", ct);
                    return;
                }

                // Assign unique player ID with default name
                var playerId = _nextPlayerId++;
                playerConn = new PlayerConn(playerId, socket, $"Player {playerId}");
                _players.Add(playerConn);
            }

            // Send ASSIGNED message with player info and waiting status
            var waitingCount = MIN_PLAYERS_TO_START - _players.Count;
            await SendAsync(socket, new 
            { 
                type = "ASSIGNED", 
                playerId = playerConn!.PlayerId, 
                sessionId = _sessionId,
                waitingFor = Math.Max(0, waitingCount),
                totalPlayers = _players.Count
            }, ct);

            // Notify other players about new joiner
            await BroadcastAsync(new 
            { 
                type = "PLAYER_JOINED", 
                payload = new 
                { 
                    playerId = playerConn.PlayerId,
                    playerName = playerConn.Name,
                    totalPlayers = _players.Count,
                    waitingFor = Math.Max(0, waitingCount)
                }
            }, ct, excludePlayer: playerConn.PlayerId);

            // Check if we should start the game
            await TryStartGameAsync(ct);

            // Begin receive loop for this player
            await ReceiveLoopAsync(playerConn!, ct);
        }

        /// <summary>
        /// Start the game if minimum players have joined.
        /// </summary>
        private async Task TryStartGameAsync(CancellationToken ct)
        {
            bool shouldStart = false;
            int[] playerIds;
            Dictionary<int, string> playerNames;

            lock (_lock)
            {
                if (_started || _players.Count < MIN_PLAYERS_TO_START)
                    return;
                
                _started = true;
                shouldStart = true;
                playerIds = _players.Select(p => p.PlayerId).ToArray();
                playerNames = _players.ToDictionary(p => p.PlayerId, p => p.Name);
            }

            if (shouldStart)
            {
                // Initialize authoritative game state with player names
                await _authoritative.InitializeAsync(_sessionId, playerIds, playerNames, ct);

                // Get daily leaderboard to include in INIT
                var dailyLeaderboard = await _authoritative.GetDailyLeaderboardAsync(ct);

                // Broadcast INIT with full game state including maze
                var initPayload = await _authoritative.GetStateAsync(_sessionId, ct);
                await BroadcastAsync(new 
                { 
                    type = "INIT", 
                    payload = initPayload,
                    dailyLeaderboard 
                }, ct);
            }
        }

        /// <summary>
        /// Main message receiving loop for a player.
        /// PDC PATTERN: Event-driven message processing
        /// </summary>
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
                // Graceful shutdown
            }
            catch (WebSocketException)
            {
                // Network error; treat as disconnect
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WS] Error in receive loop: {ex.Message}");
            }
            finally
            {
                await HandleDisconnectAsync(player, ct);
            }
        }

        /// <summary>
        /// Process incoming WebSocket message.
        /// Maps client input to authoritative game actions.
        /// </summary>
        private async Task HandleMessageAsync(int playerId, string raw, CancellationToken ct)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var type = doc.RootElement.GetProperty("type").GetString();

                // Handle pre-game messages
                if (type == "READY")
                {
                    // Player signals ready - could extend to require all players ready
                    return;
                }

                // Handle SET_NAME message - allows player to set their display name
                if (type == "SET_NAME")
                {
                    if (doc.RootElement.TryGetProperty("name", out var nameElement))
                    {
                        var name = nameElement.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            // Sanitize name (max 20 chars, no special chars)
                            name = new string(name.Take(20).Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '_').ToArray()).Trim();
                            if (!string.IsNullOrEmpty(name))
                            {
                                lock (_lock)
                                {
                                    var player = _players.FirstOrDefault(p => p.PlayerId == playerId);
                                    if (player != null)
                                    {
                                        player.Name = name;
                                    }
                                }
                                // Update name in authoritative service AND database
                                await _authoritative.SetPlayerNameAsync(_sessionId, playerId, name);
                                
                                // Broadcast name change to all players
                                await BroadcastAsync(new 
                                { 
                                    type = "NAME_CHANGED", 
                                    payload = new { playerId, name }
                                }, ct);
                            }
                        }
                    }
                    return;
                }

                if (!_started || _ended) return;

                // Map MOVE_* to direction strings
                string? dir = type switch
                {
                    "MOVE_UP" => "UP",
                    "MOVE_DOWN" => "DOWN",
                    "MOVE_LEFT" => "LEFT",
                    "MOVE_RIGHT" => "RIGHT",
                    _ => null
                };
                
                if (dir is null) return;

                // Apply move through authoritative service
                var result = await _authoritative.ApplyMoveAsync(_sessionId, playerId, dir, ct);

                // Handle rejected moves
                if (result.Status == "Rejected")
                {
                    // Only notify the player who made the rejected move
                    var rejectingPlayer = _players.FirstOrDefault(p => p.PlayerId == playerId);
                    if (rejectingPlayer != null)
                    {
                        await SendSafe(rejectingPlayer.Socket, new 
                        { 
                            type = "REJECT", 
                            payload = new { playerId, direction = dir } 
                        }, ct);
                    }
                    return;
                }

                // If flag was captured, broadcast capture event with player name
                if (result.CapturedBy.HasValue)
                {
                    var capturerName = _authoritative.GetPlayerName(_sessionId, result.CapturedBy.Value);
                    await BroadcastAsync(new 
                    { 
                        type = "FLAG_CAPTURED", 
                        payload = new 
                        { 
                            capturedBy = result.CapturedBy.Value,
                            capturedByName = capturerName,
                            newFlag = result.NewFlag.HasValue 
                                ? new { x = result.NewFlag.Value.x, y = result.NewFlag.Value.y } 
                                : null
                        }
                    }, ct);
                }

                // Broadcast updated state to all players
                var state = await _authoritative.GetStateAsync(_sessionId, ct);
                await BroadcastAsync(new { type = "STATE", payload = state }, ct);

                // Handle game completion
                if (result.Completed && !_ended)
                {
                    lock (_lock) { _ended = true; }
                    
                    // Get final leaderboard and daily leaderboard
                    var leaderboard = await _authoritative.GetLeaderboardAsync(_sessionId);
                    var dailyLeaderboard = await _authoritative.GetDailyLeaderboardAsync(ct);
                    
                    await BroadcastAsync(new 
                    { 
                        type = "GAME_OVER", 
                        payload = leaderboard,
                        dailyLeaderboard
                    }, ct);

                    // Close sockets gracefully after a delay (let clients process)
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(5000, ct); // 5 second delay
                        foreach (var p in _players.ToArray())
                        {
                            await CloseSafe(p.Socket, WebSocketCloseStatus.NormalClosure, "Game ended", ct);
                        }
                    }, ct);
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[WS] JSON parse error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WS] Message handling error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle player disconnection.
        /// PDC PATTERN: Graceful degradation under partial failures
        /// </summary>
        private async Task HandleDisconnectAsync(PlayerConn player, CancellationToken ct)
        {
            lock (_lock)
            {
                _players.RemoveAll(p => p.PlayerId == player.PlayerId);
            }

            // Notify remaining players
            await BroadcastAsync(new 
            { 
                type = "PLAYER_LEFT", 
                payload = new 
                { 
                    playerId = player.PlayerId,
                    remainingPlayers = _players.Count
                }
            }, ct);

            // If game was in progress and not enough players remain, end it
            if (_started && !_ended && _players.Count < 1)
            {
                lock (_lock) { _ended = true; }
                
                var leaderboard = await _authoritative.GetLeaderboardAsync(_sessionId);
                await BroadcastAsync(new 
                { 
                    type = "GAME_OVER", 
                    payload = new 
                    { 
                        reason = "AllPlayersLeft",
                        leaderboard
                    }
                }, ct);
            }
        }

        /// <summary>
        /// Broadcast a message to all connected players.
        /// PDC PATTERN: Parallel fan-out for efficient multi-client updates
        /// </summary>
        private async Task BroadcastAsync(object message, CancellationToken ct, int? excludePlayer = null)
        {
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);

            PlayerConn[] targets;
            lock (_lock)
            {
                targets = excludePlayer.HasValue
                    ? _players.Where(p => p.PlayerId != excludePlayer.Value).ToArray()
                    : _players.ToArray();
            }

            // Send to all targets in parallel
            var tasks = targets.Select(p => SendSafe(p.Socket, bytes, ct));
            await Task.WhenAll(tasks);
        }

        // =========================================================================
        // HELPER METHODS
        // =========================================================================

        private static Task SendAsync(WebSocket socket, object message, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            return socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }

        private static async Task SendSafe(WebSocket socket, object message, CancellationToken ct)
        {
            try { await SendAsync(socket, message, ct); }
            catch { /* Ignore send errors */ }
        }

        private static async Task SendSafe(WebSocket socket, byte[] bytes, CancellationToken ct)
        {
            try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
            catch { /* Ignore send errors */ }
        }

        private static async Task CloseSafe(WebSocket socket, WebSocketCloseStatus status, string desc, CancellationToken ct)
        {
            try { await socket.CloseAsync(status, desc, ct); }
            catch { /* Ignore close errors */ }
        }

        /// <summary>
        /// Player connection record with mutable name.
        /// </summary>
        private sealed class PlayerConn
        {
            public int PlayerId { get; }
            public WebSocket Socket { get; }
            public string Name { get; set; }
            
            public PlayerConn(int playerId, WebSocket socket, string name)
            {
                PlayerId = playerId;
                Socket = socket;
                Name = name;
            }
        }
    }
}
