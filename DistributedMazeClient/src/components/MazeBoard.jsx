// =============================================================================
// components/MazeBoard.jsx
// =============================================================================
// 3D MAZE RENDERER - React Component
// 
// Renders the game maze with:
// - 3D isometric perspective using CSS transforms
// - Support for up to 4 players with distinct colors
// - Real maze walls from server data
// - Animated flag and player markers
// - Score display on players
// =============================================================================

import React, { useMemo, useState } from "react";
import "./MazeBoard.css";

/**
 * Player color names for display
 */
const PLAYER_COLORS = {
  1: "Red",
  2: "Blue", 
  3: "Green",
  4: "Orange"
};

/**
 * MazeBoard Component
 * 
 * Renders the game maze with players and flag.
 * Uses CSS 3D transforms for isometric view.
 * 
 * @param {Array} players - Array of player objects {id, x, y, score}
 * @param {Object} flag - Flag position {x, y}
 * @param {Array} maze - 2D array of maze cells (1=path, 0=wall)
 * @param {number} currentPlayerId - The ID of the current player (for highlighting)
 * @param {Object} lastCapture - Recent flag capture info
 */
export default function MazeBoard({ 
  players = [], 
  flag = null, 
  maze = null,
  currentPlayerId = null,
  lastCapture = null
}) {
  // Toggle between 3D and flat view
  const [flatView, setFlatView] = useState(false);
  
  // Determine grid size from maze or default
  const rows = maze?.length || 21;
  const cols = maze?.[0]?.length || 21;

  // Generate cell data
  const cells = useMemo(() => {
    return Array.from({ length: rows }, (_, r) =>
      Array.from({ length: cols }, (_, c) => {
        // Check if cell is a wall (0) or path (1)
        const isWall = maze ? maze[r]?.[c] === 0 : false;
        return { r, c, isWall };
      })
    );
  }, [rows, cols, maze]);

  // Create player position map for quick lookup
  const playerMap = useMemo(() => {
    const map = new Map();
    players.forEach(p => {
      const key = `${p.x},${p.y}`;
      if (!map.has(key)) map.set(key, []);
      map.get(key).push(p);
    });
    return map;
  }, [players]);

  return (
    <div className="maze-container">
      {/* Capture notification */}
      {lastCapture && (
        <div className="capture-notification">
          🚩 Player {lastCapture.playerId} captured the flag!
        </div>
      )}
      
      {/* 3D Maze wrapper */}
      <div className={`maze-wrapper ${flatView ? 'flat-view' : ''}`}>
        <div
          className="maze-grid"
          style={{
            gridTemplateRows: `repeat(${rows}, 1fr)`,
            gridTemplateColumns: `repeat(${cols}, 1fr)`,
          }}
        >
          {cells.map((row, rIdx) =>
            row.map(({ r, c, isWall }) => {
              // Find players at this position
              // Server sends (x=column, y=row), grid indexes are (row=r, col=c)
              const playersHere = playerMap.get(`${c},${r}`) || [];
              const isFlag = flag && flag.x === c && flag.y === r;

              return (
                <div 
                  key={`${r}-${c}`} 
                  className={`maze-cell ${isWall ? 'wall' : 'path'}`}
                >
                  {/* Render players */}
                  {playersHere.map((player, idx) => (
                    <div
                      key={player.id}
                      className={`player player-${player.id} ${
                        player.id === currentPlayerId ? 'current-player' : ''
                      }`}
                      style={{
                        // Offset multiple players on same cell
                        transform: playersHere.length > 1 
                          ? `translate(${-50 + idx * 8}%, -50%) translateZ(20px)` 
                          : undefined
                      }}
                      title={`Player ${player.id} - Score: ${player.score || 0}`}
                    >
                      <span className="player-label">P{player.id}</span>
                      {player.score > 0 && (
                        <span className="player-score">{player.score}</span>
                      )}
                    </div>
                  ))}
                  
                  {/* Render flag */}
                  {isFlag && <div className="flag">🚩</div>}
                </div>
              );
            })
          )}
        </div>
      </div>

      {/* View toggle button */}
      <button 
        className="view-toggle"
        onClick={() => setFlatView(!flatView)}
      >
        {flatView ? '🎮 3D View' : '📋 Flat View'}
      </button>

      {/* Player legend */}
      <div className="maze-legend">
        {players.map(p => (
          <div key={p.id} className="legend-item">
            <div className={`legend-color player-${p.id}`} />
            <span>
              P{p.id} {p.id === currentPlayerId ? '(You)' : ''}: {p.score || 0} pts
            </span>
          </div>
        ))}
        <div className="legend-item">
          <div className="legend-color flag" style={{ background: '#ffd700' }} />
          <span>Flag</span>
        </div>
      </div>
    </div>
  );
}
