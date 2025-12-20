using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace DistributedMazeGame.Server.GameLogic
{
    public class MazeGenerator
    {
        private readonly int _rows;
        private readonly int _cols;
        private readonly int[,] _maze;

        // Thread-safe random using ThreadLocal
        private static readonly ThreadLocal<Random> _random =
            new(() => new Random(Guid.NewGuid().GetHashCode()));

        // Lock object for thread safety
        private readonly object _lock = new();

        public MazeGenerator(int rows, int cols)
        {
            if (rows <= 0 || cols <= 0)
                throw new ArgumentException("Maze dimensions must be positive.");

            _rows = rows;
            _cols = cols;
            _maze = new int[rows, cols];
        }

        /// <summary>
        /// Generate the maze using randomized DFS backtracking.
        /// </summary>
        public void Generate()
        {
            lock (_lock)
            {
                // Initialize all cells as walls (0)
                for (int r = 0; r < _rows; r++)
                    for (int c = 0; c < _cols; c++)
                        _maze[r, c] = 0;

                // Start DFS from (0,0)
                DFS(0, 0);
            }
        }

        private void DFS(int r, int c)
        {
            _maze[r, c] = 1; // Mark as path

            var directions = new List<(int dr, int dc)>
            {
                (-1, 0), (1, 0), (0, -1), (0, 1)
            };

            // Shuffle directions
            for (int i = directions.Count - 1; i > 0; i--)
            {
                int j = _random.Value!.Next(i + 1);
                (directions[i], directions[j]) = (directions[j], directions[i]);
            }

            foreach (var (dr, dc) in directions)
            {
                int nr = r + dr * 2;
                int nc = c + dc * 2;

                if (IsInBounds(nr, nc) && _maze[nr, nc] == 0)
                {
                    // Carve passage
                    _maze[r + dr, c + dc] = 1;
                    DFS(nr, nc);
                }
            }
        }

        private bool IsInBounds(int r, int c)
        {
            return r >= 0 && r < _rows && c >= 0 && c < _cols;
        }

        /// <summary>
        /// Serialize maze to JSON for WebSocket transmission.
        /// </summary>
        public string Serialize()
        {
            lock (_lock)
            {
                return JsonSerializer.Serialize(_maze);
            }
        }

        /// <summary>
        /// Get raw maze array (thread-safe copy).
        /// </summary>
        public int[,] GetMaze()
        {
            lock (_lock)
            {
                var copy = new int[_rows, _cols];
                Array.Copy(_maze, copy, _maze.Length);
                return copy;
            }
        }
    }
}
