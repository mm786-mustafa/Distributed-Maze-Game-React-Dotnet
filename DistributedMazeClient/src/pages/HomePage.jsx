// =============================================================================
// pages/HomePage.jsx
// =============================================================================
// LOBBY / HOME PAGE - Room creation and joining
// 
// Features:
// - Create a new room with auto-generated ID
// - Join existing room by ID
// - Instructions for distributed play
// =============================================================================

import React, { useState, useCallback } from "react";
import GamePage from "./GamePage";
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
  const [joined, setJoined] = useState(false);
  const [error, setError] = useState("");

  /**
   * Create a new room with random ID
   */
  const handleCreate = useCallback(() => {
    const newId = generateRoomId();
    setSessionId(newId);
    setJoined(true);
    setError("");
  }, []);

  /**
   * Join an existing room
   */
  const handleJoin = useCallback(() => {
    if (!sessionId.trim()) {
      setError("Please enter a Room ID");
      return;
    }
    setJoined(true);
    setError("");
  }, [sessionId]);

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
    return <GamePage sessionId={sessionId} onLeave={handleLeave} />;
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
