// =============================================================================
// pages/HomePage.jsx
// =============================================================================
// LOBBY / HOME PAGE - Room creation and joining
// 
// PDC CONCEPTS DEMONSTRATED:
// 1. REST API INTEGRATION - Fetches daily leaderboard from server
// 2. PLAYER IDENTITY - Allows players to set display names
// 
// Features:
// - Create a new room with auto-generated ID
// - Join existing room by ID
// - Set player display name
// - View today's top winners
// - Instructions for distributed play
// =============================================================================

import React, { useState, useCallback, useEffect } from "react";
import GamePage from "./GamePage";
import websocketService from "../services/websocketService";
import config from "../config";
import "./HomePage.css";

/**
 * Generate a random room ID
 */
function generateRoomId() {
  const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';
  let result = '';
  for (let i = 0; i < 6; i++) {
    result += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return result;
}

/**
 * HomePage Component
 * 
 * The landing page where players can create or join game rooms.
 */
export default function HomePage() {
  const [sessionId, setSessionId] = useState("");
  const [playerName, setPlayerName] = useState(() => {
    // Restore player name from localStorage if available
    return localStorage.getItem('playerName') || '';
  });
  const [joined, setJoined] = useState(false);
  const [error, setError] = useState("");
  const [dailyLeaderboard, setDailyLeaderboard] = useState([]);
  const [loadingLeaderboard, setLoadingLeaderboard] = useState(true);

  /**
   * Fetch daily leaderboard on component mount
   */
  useEffect(() => {
    const fetchDailyLeaderboard = async () => {
      try {
        const baseUrl = config.apiUrl || '';
        const response = await fetch(`${baseUrl}/api/leaderboard/daily`);
        if (response.ok) {
          const data = await response.json();
          setDailyLeaderboard(data.leaderboard || []);
        }
      } catch (err) {
        console.log('Could not fetch daily leaderboard:', err.message);
      } finally {
        setLoadingLeaderboard(false);
      }
    };
    fetchDailyLeaderboard();
  }, []);

  /**
   * Create a new room with random ID
   */
  const handleCreate = useCallback(() => {
    if (!playerName.trim()) {
      setError("Please enter your name");
      return;
    }
    // Save player name to localStorage and websocket service
    localStorage.setItem('playerName', playerName.trim());
    websocketService.setPlayerName(playerName.trim());
    
    const newId = generateRoomId();
    setSessionId(newId);
    setJoined(true);
    setError("");
  }, [playerName]);

  /**
   * Join an existing room
   */
  const handleJoin = useCallback(() => {
    if (!playerName.trim()) {
      setError("Please enter your name");
      return;
    }
    if (!sessionId.trim()) {
      setError("Please enter a Room ID");
      return;
    }
    // Save player name to localStorage and websocket service
    localStorage.setItem('playerName', playerName.trim());
    websocketService.setPlayerName(playerName.trim());
    
    setJoined(true);
    setError("");
  }, [sessionId, playerName]);

  /**
   * Leave the current room
   */
  const handleLeave = useCallback(() => {
    setJoined(false);
    setSessionId("");
    // Force page reload to disconnect WebSocket cleanly
    window.location.reload();
  }, []);

  /**
   * Handle Enter key in input
   */
  const handleKeyPress = (e) => {
    if (e.key === 'Enter') {
      handleJoin();
    }
  };

  // Show game page if joined
  if (joined) {
    return <GamePage sessionId={sessionId} playerName={playerName} onLeave={handleLeave} />;
  }

  // Show home/lobby page
  return (
    <div className="home-page">
      <div className="home-container">
        {/* Title */}
        <header className="home-header">
          <h1>🎮 Maze Capture</h1>
          <p className="subtitle">A Distributed Multiplayer Game</p>
        </header>

        {/* Player Name Input */}
        <div className="name-input-section">
          <label htmlFor="playerName">Your Name:</label>
          <input
            id="playerName"
            type="text"
            placeholder="Enter your name"
            value={playerName}
            onChange={(e) => setPlayerName(e.target.value)}
            maxLength={20}
            className="name-input"
          />
        </div>

        {/* Main actions */}
        <div className="action-cards">
          {/* Create Room Card */}
          <div className="action-card create-card">
            <div className="card-icon">🏠</div>
            <h2>Create Room</h2>
            <p>Start a new game room and invite friends</p>
            <button className="btn btn-primary" onClick={handleCreate}>
              Create New Room
            </button>
          </div>

          {/* Join Room Card */}
          <div className="action-card join-card">
            <div className="card-icon">🚪</div>
            <h2>Join Room</h2>
            <p>Enter a Room ID to join an existing game</p>
            <div className="input-group">
              <input
                type="text"
                placeholder="Enter Room ID"
                value={sessionId}
                onChange={(e) => setSessionId(e.target.value.toUpperCase())}
                onKeyPress={handleKeyPress}
                maxLength={10}
              />
              <button className="btn btn-secondary" onClick={handleJoin}>
                Join
              </button>
            </div>
            {error && <p className="error-msg">{error}</p>}
          </div>
        </div>

        {/* Daily Leaderboard */}
        <section className="daily-leaderboard-section">
          <h3>🏆 Today's Top Winners</h3>
          {loadingLeaderboard ? (
            <p className="loading-text">Loading leaderboard...</p>
          ) : dailyLeaderboard.length > 0 ? (
            <div className="daily-leaderboard-table">
              <div className="leaderboard-header">
                <span className="col-rank">Rank</span>
                <span className="col-name">Player</span>
                <span className="col-wins">Wins</span>
                <span className="col-flags">Flags</span>
                <span className="col-games">Games</span>
              </div>
              {dailyLeaderboard.map((entry, idx) => (
                <div key={entry.playerId} className="leaderboard-row">
                  <span className="col-rank">
                    {idx === 0 ? '🥇' : idx === 1 ? '🥈' : idx === 2 ? '🥉' : `#${idx + 1}`}
                  </span>
                  <span className="col-name">{entry.playerName}</span>
                  <span className="col-wins">{entry.wins}</span>
                  <span className="col-flags">{entry.totalFlags}</span>
                  <span className="col-games">{entry.gamesPlayed}</span>
                </div>
              ))}
            </div>
          ) : (
            <p className="no-data-text">No winners yet today. Be the first!</p>
          )}
        </section>

        {/* Game Info */}
        <section className="game-info">
          <h3>How to Play</h3>
          <div className="info-grid">
            <div className="info-item">
              <span className="info-icon">👥</span>
              <div>
                <strong>2-4 Players</strong>
                <p>Compete with friends on different computers</p>
              </div>
            </div>
            <div className="info-item">
              <span className="info-icon">🚩</span>
              <div>
                <strong>Capture Flags</strong>
                <p>Race to capture 10 flags to win</p>
              </div>
            </div>
            <div className="info-item">
              <span className="info-icon">⌨️</span>
              <div>
                <strong>WASD Controls</strong>
                <p>Navigate through the maze</p>
              </div>
            </div>
            <div className="info-item">
              <span className="info-icon">🏆</span>
              <div>
                <strong>Real-time Competition</strong>
                <p>Server authoritative gameplay</p>
              </div>
            </div>
          </div>
        </section>

        {/* Distributed Setup Info */}
        <section className="setup-info">
          <h3>🖥️ Distributed Setup</h3>
          <div className="setup-steps">
            <div className="step">
              <span className="step-num">1</span>
              <p><strong>Server:</strong> Run the game server on one PC</p>
            </div>
            <div className="step">
              <span className="step-num">2</span>
              <p><strong>Clients:</strong> Open this page on other PCs using the server's IP</p>
            </div>
            <div className="step">
              <span className="step-num">3</span>
              <p><strong>Play:</strong> Create a room, share the ID, and start playing!</p>
            </div>
          </div>
        </section>

        {/* Footer */}
        <footer className="home-footer">
          <p>PDC Project - Parallel and Distributed Computing</p>
          <p className="tech-stack">ASP.NET Core • WebSockets • React • Real-time Sync</p>
        </footer>
      </div>
    </div>
  );
}
