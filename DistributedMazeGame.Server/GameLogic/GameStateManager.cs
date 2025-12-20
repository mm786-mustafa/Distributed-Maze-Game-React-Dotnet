// GameLogic/GameStateManager.cs
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;

namespace DistributedMazeGame.Server.GameLogic
{
    public record PlayerState(int PlayerId, int X, int Y);
    public record GameBroadcast(string Type, object Payload);
    public enum MoveResult { Accepted, Rejected, Completed }

    public sealed class GameStateManager
    {
        // Single-threaded authority: all mutations are performed on a dedicated worker thread.
        // Other threads only enqueue inputs and consume broadcasts.

        private readonly int[,] _maze;
        private readonly Dictionary<int, PlayerState> _players = new(); // playerId -> position
        private readonly object _snapshotLock = new(); // protects snapshot reads
        private readonly Channel<GameBroadcast> _broadcastChannel;

        public int SessionId { get; }
        public (int X, int Y) Flag { get; private set; }
        public bool Completed { get; private set; }

        public GameStateManager(int sessionId, int[,] maze, Channel<GameBroadcast> broadcastChannel)
        {
            SessionId = sessionId;
            _maze = maze;
            _broadcastChannel = broadcastChannel;
        }

        // Initialize players at opposite ends and place flag at a valid cell
        public void Initialize(int playerAId, int playerBId)
        {
            // Top-left and bottom-right (walkable) start positions
            var startA = FindFirstWalkable(0, 0, stepX: 1, stepY: 1);
            var startB = FindFirstWalkable(_maze.GetLength(0) - 1, _maze.GetLength(1) - 1, stepX: -1, stepY: -1);

            _players[playerAId] = new PlayerState(playerAId, startA.X, startA.Y);
            _players[playerBId] = new PlayerState(playerBId, startB.X, startB.Y);

            // Place flag at a valid cell roughly centered or fallback to nearest walkable
            var midX = _maze.GetLength(0) / 2;
            var midY = _maze.GetLength(1) / 2;
            Flag = IsWalkable(midX, midY) ? (midX, midY) : FindNearestWalkable(midX, midY);

            BroadcastState("INIT");
        }

        // Authoritative move validation and application
        public MoveResult TryApplyMove(int playerId, string direction, out PlayerState? updated)
        {
            updated = null;
            if (Completed || !_players.ContainsKey(playerId)) return MoveResult.Rejected;

            var p = _players[playerId];
            var (dx, dy) = direction switch
            {
                "UP" => (0, -1),
                "DOWN" => (0, 1),
                "LEFT" => (-1, 0),
                "RIGHT" => (1, 0),
                _ => (0, 0)
            };

            var nx = p.X + dx;
            var ny = p.Y + dy;

            // Validate walkability
            if (!IsWalkable(nx, ny))
            {
                // Broadcast rejection
                _broadcastChannel.Writer.TryWrite(new GameBroadcast("REJECT", new
                {
                    sessionId = SessionId, playerId, direction
                }));
                return MoveResult.Rejected;
            }

            // Apply move
            var newState = new PlayerState(playerId, nx, ny);
            _players[playerId] = newState;
            updated = newState;

            // Check win
            if (nx == Flag.X && ny == Flag.Y)
            {
                Completed = true;
                BroadcastState("MOVE");
                _broadcastChannel.Writer.TryWrite(new GameBroadcast("WIN", new
                {
                    sessionId = SessionId,
                    winnerPlayerId = playerId,
                    flagX = Flag.X,
                    flagY = Flag.Y
                }));
                return MoveResult.Completed;
            }

            // Broadcast accepted move and new positions
            BroadcastState("MOVE");
            return MoveResult.Accepted;
        }

        public (int X, int Y) GetFlag()
        {
            lock (_snapshotLock) { return Flag; }
        }

        public int[,] SnapshotMaze()
        {
            lock (_snapshotLock)
            {
                return (int[,])_maze.Clone();
            }
        }

        private void BroadcastState(string type)
        {
            // Prepare minimal payload for WebSocket clients
            var playersPayload = new List<object>();
            foreach (var kv in _players)
                playersPayload.Add(new { id = kv.Key, x = kv.Value.X, y = kv.Value.Y });

            _broadcastChannel.Writer.TryWrite(new GameBroadcast(type, new
            {
                sessionId = SessionId,
                flag = new { x = Flag.X, y = Flag.Y },
                players = playersPayload
            }));
        }

        private bool IsWalkable(int x, int y)
        {
            if (x < 0 || y < 0 || x >= _maze.GetLength(0) || y >= _maze.GetLength(1)) return false;
            return _maze[x, y] == 1;
        }

        private (int X, int Y) FindFirstWalkable(int startX, int startY, int stepX, int stepY)
        {
            var x = startX; var y = startY;
            while (x >= 0 && y >= 0 && x < _maze.GetLength(0) && y < _maze.GetLength(1))
            {
                if (IsWalkable(x, y)) return (x, y);
                x += stepX; y += stepY;
            }
            // Fallback to nearest walkable to origin
            return FindNearestWalkable(Math.Clamp(startX, 0, _maze.GetLength(0)-1),
                                       Math.Clamp(startY, 0, _maze.GetLength(1)-1));
        }

        private (int X, int Y) FindNearestWalkable(int cx, int cy)
        {
            int maxR = Math.Max(_maze.GetLength(0), _maze.GetLength(1));
            for (int r = 0; r < maxR; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int dy = r - Math.Abs(dx);
                    foreach (var sign in new[] { -1, 1 })
                    {
                        var x = cx + dx;
                        var y = cy + sign * dy;
                        if (IsWalkable(x, y)) return (x, y);
                    }
                }
            }
            return (cx, cy); // as last resort
        }
    }
}
