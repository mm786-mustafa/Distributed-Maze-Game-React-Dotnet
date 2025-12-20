import { useState, useEffect } from "react";
import websocketService from "../services/websocketService";

export default function useGameState(sessionId) {
  const [players, setPlayers] = useState([]);
  const [flag, setFlag] = useState(null);
  const [winner, setWinner] = useState(null);
  const [playerId, setPlayerId] = useState(null);

  useEffect(() => {
    if (!sessionId) return;
    websocketService.connect(sessionId);

    const onInit = (payload) => {
      setPlayers(payload.players || []);
      setFlag(payload.flag || null);
    };
    const onState = (payload) => {
      setPlayers(payload.players || []);
    };
    const onEnd = (payload) => {
      setWinner(payload?.winnerPlayerId ?? null);
    };
    const onAssigned = (payload) => {
      if (payload && payload.playerId != null) setPlayerId(payload.playerId);
    };

    websocketService.subscribe("INIT", onInit);
    websocketService.subscribe("STATE", onState);
    websocketService.subscribe("END", onEnd);
    websocketService.subscribe("ASSIGNED", onAssigned);

    return () => {
      websocketService.unsubscribe("INIT", onInit);
      websocketService.unsubscribe("STATE", onState);
      websocketService.unsubscribe("END", onEnd);
      websocketService.unsubscribe("ASSIGNED", onAssigned);
    };
  }, [sessionId]);

  return { players, flag, winner, playerId };
}
