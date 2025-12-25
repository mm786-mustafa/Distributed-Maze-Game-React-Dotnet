// =============================================================================
// hooks/useGameState.js
// =============================================================================
// CLIENT STATE MANAGEMENT - React Hook for Game State
// 
// PDC CONCEPTS DEMONSTRATED:
// 1. CLIENT-SIDE STATE SYNCHRONIZATION - Mirrors authoritative server state
// 2. EVENT-DRIVEN UPDATES - Reacts to server broadcasts
// 3. OPTIMISTIC UI - Shows local changes while awaiting server confirmation
// 4. DAILY LEADERBOARD - Displays today's top winners
// =============================================================================

import { useState, useEffect, useCallback } from "react";
import websocketService from "../services/websocketService";

/**
 * Custom React hook for managing game state.
 * Subscribes to WebSocket events and maintains synchronized client state.
 * 
 * @param {string} sessionId - The game session/room ID
 * @returns {object} Game state including players, flags, scores, and game status
 */
export default function useGameState(sessionId) {
  // Core game state
  const [players, setPlayers] = useState([]);
  const [flag, setFlag] = useState(null);
  const [maze, setMaze] = useState(null);
  const [playerId, setPlayerId] = useState(null);
  
  // Scoring and leaderboard
  const [leaderboard, setLeaderboard] = useState([]);
  const [flagsCaptured, setFlagsCaptured] = useState(0);
  const [totalFlags, setTotalFlags] = useState(10);
  
  // Daily leaderboard (today's top winners)
  const [dailyLeaderboard, setDailyLeaderboard] = useState([]);
  
  // Game flow state
  const [gameStatus, setGameStatus] = useState("connecting"); // connecting, waiting, playing, ended
  const [waitingFor, setWaitingFor] = useState(0);
  const [winner, setWinner] = useState(null);
  const [winnerName, setWinnerName] = useState(null);
  const [gameOverData, setGameOverData] = useState(null);
  
  // Flag capture notification
  const [lastCapture, setLastCapture] = useState(null);

  useEffect(() => {
    if (!sessionId) return;
    
    // Connect to WebSocket server
    websocketService.connect(sessionId);

    // =========================================================================
    // EVENT HANDLERS - Process server broadcasts
    // =========================================================================

    /**
     * ASSIGNED: Server confirms player joined and assigns ID
     */
    const onAssigned = (payload) => {
      if (payload?.playerId != null) {
        setPlayerId(payload.playerId);
        setWaitingFor(payload.waitingFor ?? 0);
        setGameStatus(payload.waitingFor > 0 ? "waiting" : "playing");
      }
    };

    /**
     * PLAYER_JOINED: Another player joined the session
     */
    const onPlayerJoined = (payload) => {
      setWaitingFor(payload.waitingFor ?? 0);
      if (payload.waitingFor <= 0) {
        setGameStatus("playing");
      }
    };

    /**
     * PLAYER_LEFT: A player disconnected
     */
    const onPlayerLeft = (payload) => {
      console.log(`Player ${payload.playerId} left. Remaining: ${payload.remainingPlayers}`);
    };

    /**
     * NAME_CHANGED: A player changed their name
     */
    const onNameChanged = (payload) => {
      setPlayers(prev => prev.map(p => 
        p.id === payload.playerId ? { ...p, name: payload.name } : p
      ));
      setLeaderboard(prev => prev.map(entry =>
        entry.playerId === payload.playerId ? { ...entry, name: payload.name } : entry
      ));
    };

    /**
     * INIT: Game started - receive initial state with maze and positions
     */
    const onInit = (payload) => {
      setPlayers(payload.players || []);
      setFlag(payload.flag || null);
      setMaze(payload.maze || null);
      setLeaderboard(payload.leaderboard || []);
      setFlagsCaptured(payload.flagsCaptured || 0);
      setTotalFlags(payload.totalFlags || 10);
      setGameStatus("playing");
    };

    /**
     * DAILY_LEADERBOARD: Received daily leaderboard data
     */
    const onDailyLeaderboard = (payload) => {
      if (payload?.leaderboard) {
        setDailyLeaderboard(payload.leaderboard);
      }
    };

    /**
     * STATE: Regular state update (after any player moves)
     */
    const onState = (payload) => {
      setPlayers(payload.players || []);
      if (payload.flag) setFlag(payload.flag);
      if (payload.leaderboard) setLeaderboard(payload.leaderboard);
      if (payload.flagsCaptured != null) setFlagsCaptured(payload.flagsCaptured);
    };

    /**
     * FLAG_CAPTURED: A player captured the flag
     */
    const onFlagCaptured = (payload) => {
      setLastCapture({
        playerId: payload.capturedBy,
        playerName: payload.capturedByName || `Player ${payload.capturedBy}`,
        timestamp: Date.now()
      });
      
      // Update flag position if new flag spawned
      if (payload.newFlag) {
        setFlag(payload.newFlag);
      }
      
      // Clear notification after 2 seconds
      setTimeout(() => setLastCapture(null), 2000);
    };

    /**
     * GAME_OVER: Game ended - show final leaderboard
     */
    const onGameOver = (payload) => {
      setGameStatus("ended");
      setGameOverData(payload);
      
      if (payload.winnerId != null) {
        setWinner(payload.winnerId);
        setWinnerName(payload.winnerName || `Player ${payload.winnerId}`);
      }
      
      if (payload.leaderboard) {
        setLeaderboard(payload.leaderboard);
      }
    };

    /**
     * ERROR: Server sent an error
     */
    const onError = (payload) => {
      console.error("Server error:", payload.message);
    };

    // Subscribe to all events
    websocketService.subscribe("ASSIGNED", onAssigned);
    websocketService.subscribe("PLAYER_JOINED", onPlayerJoined);
    websocketService.subscribe("PLAYER_LEFT", onPlayerLeft);
    websocketService.subscribe("NAME_CHANGED", onNameChanged);
    websocketService.subscribe("INIT", onInit);
    websocketService.subscribe("DAILY_LEADERBOARD", onDailyLeaderboard);
    websocketService.subscribe("STATE", onState);
    websocketService.subscribe("FLAG_CAPTURED", onFlagCaptured);
    websocketService.subscribe("GAME_OVER", onGameOver);
    websocketService.subscribe("ERROR", onError);

    // Cleanup on unmount
    return () => {
      websocketService.unsubscribe("ASSIGNED", onAssigned);
      websocketService.unsubscribe("PLAYER_JOINED", onPlayerJoined);
      websocketService.unsubscribe("PLAYER_LEFT", onPlayerLeft);
      websocketService.unsubscribe("NAME_CHANGED", onNameChanged);
      websocketService.unsubscribe("INIT", onInit);
      websocketService.unsubscribe("DAILY_LEADERBOARD", onDailyLeaderboard);
      websocketService.unsubscribe("STATE", onState);
      websocketService.unsubscribe("FLAG_CAPTURED", onFlagCaptured);
      websocketService.unsubscribe("GAME_OVER", onGameOver);
      websocketService.unsubscribe("ERROR", onError);
    };
  }, [sessionId]);

  /**
   * Get current player's score
   */
  const myScore = players.find(p => p.id === playerId)?.score ?? 0;

  /**
   * Get current player's name
   */
  const myName = players.find(p => p.id === playerId)?.name ?? `Player ${playerId}`;

  /**
   * Check if current player is winning
   */
  const isWinning = leaderboard.length > 0 && leaderboard[0]?.playerId === playerId;

  return {
    // Core state
    players,
    flag,
    maze,
    playerId,
    
    // Scoring
    leaderboard,
    flagsCaptured,
    totalFlags,
    myScore,
    myName,
    isWinning,
    
    // Daily leaderboard
    dailyLeaderboard,
    
    // Game flow
    gameStatus,
    waitingFor,
    winner,
    winnerName,
    gameOverData,
    
    // Notifications
    lastCapture
  };
}
