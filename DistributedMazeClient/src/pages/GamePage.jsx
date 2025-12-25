// =============================================================================
// pages/GamePage.jsx
// =============================================================================
// MAIN GAME PAGE - Renders the active game session
// 
// PDC CONCEPTS DEMONSTRATED:
// 1. REAL-TIME STATE SYNCHRONIZATION - UI updates from server broadcasts
// 2. HUD SCOREBOARD - Live scores synchronized across all clients
// 3. DAILY LEADERBOARD - Shows today's top winners
// 
// Shows:
// - Waiting room while players join
// - Active game with maze, players, and scoreboard
// - Final leaderboard when game ends
// - Today's top winners
// =============================================================================

import React from "react";
import useGameState from "../hooks/useGameState";
import useKeyboardControls from "../hooks/useKeyboardControls";
import MazeBoard from "../components/MazeBoard";
import "./GamePage.css";

/**
 * GamePage Component
 * 
 * The main game view that handles all game states:
 * - Waiting for players
 * - Active gameplay
 * - Game over / leaderboard
 */
export default function GamePage({ sessionId, playerName, onLeave }) {
  // Get all game state from the hook
  const {
    players,
    flag,
    maze,
    playerId,
    leaderboard,
    flagsCaptured,
    totalFlags,
    myScore,
    myName,
    isWinning,
    gameStatus,
    waitingFor,
    winner,
    winnerName,
    gameOverData,
    lastCapture,
    dailyLeaderboard
  } = useGameState(sessionId);

  // Enable keyboard controls unless game ended
  useKeyboardControls(sessionId, playerId, gameStatus === "ended");

  // =========================================================================
  // RENDER: WAITING ROOM
  // =========================================================================
  if (gameStatus === "waiting" || gameStatus === "connecting") {
    return (
      <div className="game-page">
        <div className="waiting-room">
          <h1>🎮 Maze Capture</h1>
          <div className="room-info">
            <p className="room-id">Room ID: <strong>{sessionId}</strong></p>
            <p className="player-id">
              You are: <strong>{playerName || myName || `Player ${playerId || "..."}`}</strong>
            </p>
          </div>
          
          <div className="waiting-status">
            {gameStatus === "connecting" ? (
              <>
                <div className="spinner"></div>
                <p>Connecting to server...</p>
              </>
            ) : (
              <>
                <div className="spinner"></div>
                <p>Waiting for {waitingFor} more player{waitingFor > 1 ? 's' : ''}...</p>
                <p className="hint">Share the Room ID with friends to join!</p>
              </>
            )}
          </div>

          {/* Daily Leaderboard in Waiting Room */}
          {dailyLeaderboard && dailyLeaderboard.length > 0 && (
            <div className="daily-leaders-card">
              <h3>🏆 Today's Top Winners</h3>
              <div className="daily-leaderboard">
                {dailyLeaderboard.slice(0, 5).map((entry, idx) => (
                  <div key={entry.playerId} className="daily-entry">
                    <span className="daily-rank">
                      {idx === 0 ? '🥇' : idx === 1 ? '🥈' : idx === 2 ? '🥉' : `#${idx + 1}`}
                    </span>
                    <span className="daily-name">{entry.playerName}</span>
                    <span className="daily-wins">{entry.wins} win{entry.wins !== 1 ? 's' : ''}</span>
                  </div>
                ))}
              </div>
            </div>
          )}

          <button className="leave-btn" onClick={onLeave}>Leave Room</button>
        </div>
      </div>
    );
  }

  // =========================================================================
  // RENDER: GAME OVER
  // =========================================================================
  if (gameStatus === "ended") {
    const isWinner = winner === playerId;
    const finalLeaderboard = gameOverData?.leaderboard || leaderboard;
    
    return (
      <div className="game-page">
        <div className="game-over">
          <h1>{isWinner ? "🏆 Victory!" : "Game Over"}</h1>
          
          <div className="winner-announcement">
            <p className="winner-text">
              Winner: <strong>{winnerName || `Player ${winner}`}</strong>
            </p>
          </div>
          
          <div className="final-results">
            <h2>Final Leaderboard</h2>
            <div className="leaderboard final">
              {finalLeaderboard.map((entry, idx) => (
                <div 
                  key={entry.playerId} 
                  className={`leaderboard-entry ${entry.playerId === playerId ? 'you' : ''} ${idx === 0 ? 'winner' : ''}`}
                >
                  <span className="rank">
                    {idx === 0 ? '🥇' : idx === 1 ? '🥈' : idx === 2 ? '🥉' : `#${idx + 1}`}
                  </span>
                  <span className="player-name">
                    {entry.name || `Player ${entry.playerId}`}
                    {entry.playerId === playerId && ' (You)'}
                  </span>
                  <span className="score">{entry.score} flags</span>
                </div>
              ))}
            </div>
            
            {gameOverData?.duration && (
              <p className="game-duration">
                Game Duration: {Math.floor(gameOverData.duration / 60)}:{(gameOverData.duration % 60).toString().padStart(2, '0')}
              </p>
            )}
          </div>

          {/* Daily Leaderboard on Game Over */}
          {dailyLeaderboard && dailyLeaderboard.length > 0 && (
            <div className="daily-results">
              <h2>🏆 Today's Top Winners</h2>
              <div className="leaderboard daily">
                {dailyLeaderboard.slice(0, 5).map((entry, idx) => (
                  <div key={entry.playerId} className="leaderboard-entry daily-entry">
                    <span className="rank">
                      {idx === 0 ? '🥇' : idx === 1 ? '🥈' : idx === 2 ? '🥉' : `#${idx + 1}`}
                    </span>
                    <span className="player-name">{entry.playerName}</span>
                    <span className="score">
                      {entry.wins} win{entry.wins !== 1 ? 's' : ''} 
                      <small>({entry.totalFlags} flags)</small>
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}

          <button className="play-again-btn" onClick={onLeave}>Play Again</button>
        </div>
      </div>
    );
  }

  // =========================================================================
  // RENDER: ACTIVE GAME
  // =========================================================================
  return (
    <div className="game-page">
      {/* Header with game info */}
      <header className="game-header">
        <div className="header-left">
          <h1>🎮 Maze Capture</h1>
          <span className="room-badge">Room: {sessionId}</span>
        </div>
        <div className="header-center">
          <div className="flag-counter">
            <span className="flag-icon">🚩</span>
            <span className="flag-count">{flagsCaptured} / {totalFlags}</span>
          </div>
        </div>
        <div className="header-right">
          <span className="player-badge">
            You: {playerName || myName}
            {isWinning && ' 👑'}
          </span>
        </div>
      </header>

      {/* Main game area */}
      <div className="game-content">
        {/* Scoreboard sidebar */}
        <aside className="scoreboard">
          <h3>🏆 Live Scoreboard</h3>
          <div className="leaderboard">
            {leaderboard.map((entry, idx) => (
              <div 
                key={entry.playerId} 
                className={`leaderboard-entry ${entry.playerId === playerId ? 'you' : ''}`}
              >
                <span className="rank">#{entry.rank}</span>
                <span className={`player-color color-${entry.playerId}`}></span>
                <span className="player-name">
                  {entry.name || `P${entry.playerId}`}
                  {entry.playerId === playerId && ' (You)'}
                </span>
                <span className="score">{entry.score}</span>
              </div>
            ))}
          </div>
          
          <div className="your-score">
            <h4>Your Score</h4>
            <span className="big-score">{myScore}</span>
          </div>

          {/* Mini Daily Leaderboard */}
          {dailyLeaderboard && dailyLeaderboard.length > 0 && (
            <div className="daily-mini">
              <h4>🏆 Today's Leaders</h4>
              <div className="daily-mini-list">
                {dailyLeaderboard.slice(0, 3).map((entry, idx) => (
                  <div key={entry.playerId} className="daily-mini-entry">
                    <span className="mini-rank">
                      {idx === 0 ? '🥇' : idx === 1 ? '🥈' : '🥉'}
                    </span>
                    <span className="mini-name">{entry.playerName}</span>
                    <span className="mini-wins">{entry.wins}W</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </aside>

        {/* Maze board */}
        <main className="maze-area">
          <MazeBoard 
            players={players} 
            flag={flag} 
            maze={maze}
            currentPlayerId={playerId}
            lastCapture={lastCapture}
          />
        </main>

        {/* Controls info */}
        <aside className="controls-info">
          <h3>⌨️ Controls</h3>
          <div className="control-keys">
            <div className="key-row">
              <span className="key">W</span>
            </div>
            <div className="key-row">
              <span className="key">A</span>
              <span className="key">S</span>
              <span className="key">D</span>
            </div>
          </div>
          <p className="controls-hint">Use WASD or Arrow Keys to move</p>
          
          <div className="objective">
            <h4>🎯 Objective</h4>
            <p>Capture {totalFlags} flags to win!</p>
            <p>Race other players to reach the flag first.</p>
          </div>
        </aside>
      </div>
    </div>
  );
}
