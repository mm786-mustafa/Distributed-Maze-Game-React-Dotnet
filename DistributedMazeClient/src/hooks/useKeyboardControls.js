import { useEffect, useRef } from "react";
import websocketService from "../services/websocketService";

export default function useKeyboardControls(sessionId, playerId, gameEnded) {
  const lastSentRef = useRef(0);
  const throttleMs = 150; // minimum delay between inputs

  useEffect(() => {
    if (gameEnded) return; // disable input when game ends

    const handleKeyDown = (e) => {
      const now = Date.now();
      if (now - lastSentRef.current < throttleMs) return; // throttle

      let type = null;
      switch (e.key) {
        case "ArrowUp":
        case "w":
        case "W":
          type = "MOVE_UP";
          break;
        case "ArrowDown":
        case "s":
        case "S":
          type = "MOVE_DOWN";
          break;
        case "ArrowLeft":
        case "a":
        case "A":
          type = "MOVE_LEFT";
          break;
        case "ArrowRight":
        case "d":
        case "D":
          type = "MOVE_RIGHT";
          break;
        default:
          break;
      }

      if (type) {
        websocketService.send(type, { sessionId, playerId });
        lastSentRef.current = now;
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [sessionId, playerId, gameEnded]);
}
