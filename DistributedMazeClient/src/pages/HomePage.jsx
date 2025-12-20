import React, { useState } from "react";
import GamePage from "./GamePage";

export default function HomePage() {
  const [sessionId, setSessionId] = useState("");
  const [joined, setJoined] = useState(false);

  const handleJoin = () => {
    setJoined(true);
  };

  return (
    <div>
      {!joined ? (
        <>
          <h1>Join Maze Game</h1>
          <input
            type="text"
            placeholder="Enter session ID"
            value={sessionId}
            onChange={(e) => setSessionId(e.target.value)}
          />
          <button onClick={handleJoin}>Join</button>
        </>
      ) : (
        <GamePage sessionId={sessionId} />
      )}
    </div>
  );
}
