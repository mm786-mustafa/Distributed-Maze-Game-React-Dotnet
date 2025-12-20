import React from "react";
import useGameState from "../hooks/useGameState";
import useKeyboardControls from "../hooks/useKeyboardControls";
import MazeBoard from "../components/MazeBoard";

export default function GamePage({ sessionId }) {
  const { players, flag, winner, playerId } = useGameState(sessionId);

  // Enable keyboard controls unless game ended
  useKeyboardControls(sessionId, playerId, !!winner);

  return (
    <div>
      <h1>Distributed Maze Game</h1>
      <MazeBoard players={players} flag={flag} rows={21} cols={21} />
      {winner ? (
        winner === playerId ? (
          <h2 style={{ color: "green" }}>🏆 You Win!</h2>
        ) : (
          <h2 style={{ color: "red" }}>❌ You Lose!</h2>
        )
      ) : (
        <h2>Game in progress...</h2>
      )}
    </div>
  );
}
