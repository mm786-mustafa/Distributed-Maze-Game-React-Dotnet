import React, { useMemo } from "react";
import "./MazeBoard.css"; // optional CSS file for styling

// Use a fixed grid size to prevent jitter and uneven cells.
export default function MazeBoard({ players = [], flag = null, rows = 21, cols = 21 }) {
  const cells = useMemo(() => {
    return Array.from({ length: rows }, (_, r) =>
      Array.from({ length: cols }, (_, c) => ({ r, c }))
    );
  }, [rows, cols]);

  return (
    <div className="maze-container">
      <div
        className="maze-grid"
        style={{
          gridTemplateRows: `repeat(${rows}, 1fr)`,
          gridTemplateColumns: `repeat(${cols}, 1fr)`,
        }}
      >
        {cells.map((row, rIdx) =>
          row.map(({ r, c }) => {
            // Server uses (x, y) => (column, row). Grid indexes are (row=r, col=c).
            const playerHere = players.find((p) => p.x === c && p.y === r);
            const isFlag = flag && flag.x === c && flag.y === r;

            let cellClass = "maze-cell path"; // open grid as server sends no walls

            return (
              <div key={`${r}-${c}`} className={cellClass}>
                {playerHere && (
                  <div
                    className={`player player-${playerHere.id}`}
                    title={`Player ${playerHere.id}`}
                  />
                )}
                {isFlag && <div className="flag">🚩</div>}
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}
