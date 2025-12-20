import React from "react";

export default function PlayerStatus({ winner }) {
  return (
    <div>
      {winner ? <h2>Winner: Player {winner}</h2> : <h2>Game in progress...</h2>}
    </div>
  );
}
