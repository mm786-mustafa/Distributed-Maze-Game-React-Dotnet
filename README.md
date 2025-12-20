# Distributed Maze Game
A real-time, two-player maze game:

Authoritative .NET 9 WebSocket server (EF Core + MySQL) manages sessions, movement, and persistence.
Vite + React client renders the board and connects via WebSockets.
Players join the same session (room) and use WASD to move; first to reach the flag wins.
Key features

Authoritative game logic on the server for fair play.
WebSocket protocol: ASSIGNED, INIT, STATE, END broadcasts; clients send MOVE directions.
EF Core + MySQL persistence: sessions, players, moves, and results.
LAN-ready: server on one machine, clients on others.
Diagnostics endpoint to list active sessions.
Tech stack

Server: .NET 9, ASP.NET Core, EF Core, Pomelo MySQL provider, WebSockets
Client: Vite, React
Database: MySQL 8.x
Project structure

Server (C#): DistributedMazeGame.Server

Program.cs – DI, WebSockets, EF Core setup
Properties/launchSettings.json – dev profiles (binds to port 5000)
Controller/SessionsController.cs – diagnostics endpoints (list sessions)
Networking/
WebSocketSessionManager.cs – tracks connections per session, assigns player IDs
GameWebSocketAdapter.cs, WebSocketHandler.cs – message handling
Contracts.cs – message shapes/types
Services/
GameAuthoritativeService.cs – in-memory state, movement, win detection, async persistence
GameAuthoritativeWorker.cs, GameWorker.cs, MoveLogger.cs – background tasks/helpers
Data/
ApplicationDbContext.cs – DbSets and EF configuration
DesignTimeApplicationDbContextFactory.cs – EF migrations support without DB auto-detect
Configurations/*.cs – entity Fluent API configuration
Entities/
GameSession.cs, Player.cs, Move.cs, Result.cs
Migrations/ – generated EF migrations
GameLogic/ – session state, maze generator (currently using a simple open grid)
Client (React): c:\Users\Mustafa\Desktop\Project\PDC-Project\DistributedMazeClient

vite.config.js, index.html, package.json – Vite setup
src/
App.jsx, main.jsx – app entry
pages/HomePage.jsx – join a session
pages/GamePage.jsx – renders MazeBoard and player info
components/MazeBoard.jsx, MazeBoard.css – fixed 21x21 board, correct x/y mapping
components/PlayerStatus.jsx – player info
hooks/useGameState.js – single WebSocket connection, subscribes to server messages
hooks/useKeyboardControls.js – maps WASD to directions
services/websocketService.js – event-based WS client with reconnect
config.js – reads VITE_BACKEND_URL or REACT_APP_BACKEND_URL
Data model

GameSession
SessionId (PK), StartTime, EndTime, Status ("Active"/"Completed")
Player
PlayerId (PK), Name, ConnectedAt
Move
MoveId (PK), SessionId (FK → GameSession), PlayerId (FK → Player), Direction ("UP"/"DOWN"/"LEFT"/"RIGHT"), Timestamp
Result
ResultId (PK), SessionId (FK → GameSession), WinnerPlayerId, Duration (seconds)
Server WebSocket protocol

Client → Server
Connect: ws://<server-ip>:5000/ws?sessionId=<room-id>
Send move: { type: "MOVE", playerId: <1|2>, direction: "UP"|"DOWN"|"LEFT"|"RIGHT" }
Server → Clients (broadcast per session)
ASSIGNED: { type: "ASSIGNED", playerId: <1|2> }
INIT: { type: "INIT", sessionId, flag: { x, y }, players: [{ id, x, y }] }
STATE: { type: "STATE", sessionId, flag: { x, y }, players: [{ id, x, y }] }
END: { type: "END", sessionId, winnerPlayerId }
Gameplay specifics

Board: fixed 21x21 open grid (no walls); can be swapped to MazeGenerator later.
Coordinates: x = column (left→right), y = row (top→bottom).
Controls:
W → UP (y - 1)
S → DOWN (y + 1)
A → LEFT (x - 1)
D → RIGHT (x + 1)
Winning: reaching the flag (center) ends the session and persists Result.
Setup

Prerequisites

.NET 9 SDK
Node.js 18+ and npm
MySQL 8.x
Database (MySQL)

Create database and user:
Configure connection string:
appsettings.Development.json or environment variable MAZE_DB:
server=localhost;port=3306;database=MazeGame;user=maze_user;password=StrongPass123;
Optional if needed: AllowPublicKeyRetrieval=true;SslMode=None;
Apply EF migrations:
Run locally (server and two clients on one machine)

Server:
Client:
Open two browsers to the Vite URL, enter the same session ID, play.
Run across multiple machines (LAN)

On the server machine:
Ensure firewall allows inbound TCP 5000.
Run server (as above).
On each client machine:
Set DistributedMazeClient/.env.development:
VITE_BACKEND_URL=ws://<server-ip>:5000/ws
Run Vite and open the local dev URL (e.g., http://localhost:5173).
Join the same session ID on both clients.
Verify connections

Server diagnostics:
Browser devtools → Network → WS → verify frames (ASSIGNED, INIT, STATE, END).
Troubleshooting

Access denied for MySQL:
Fix user/password and grants (see Database section).
Consider using MAZE_DB env var to override conn string at runtime.
Foreign key constraint on Moves:
Ensure Players rows exist. The server creates players on session init and before move persistence; if schema was changed, re-run migrations.
Wrong movement or jitter:
Client board uses fixed 21x21 and correct x/y mapping; ensure both clients are on the latest build.
Port mismatch:
launchSettings.json binds to http://0.0.0.0:5000; override with:
WebSockets blocked:
Check corporate proxies/firewall; use ws:// over LAN or wss:// behind a reverse proxy in production.
Development notes

EF design-time factory avoids ServerVersion.AutoDetect during migrations.
Server persists session start, move logs, and results asynchronously to keep gameplay responsive.
Session capacity is 2 players; joining a full session is rejected.
No authentication; session IDs are user-supplied room strings.
License and contributions

Add your preferred license (MIT recommended).
Contributions: open an issue or PR; include steps to reproduce and logs for server/client.
Optional enhancements

Dynamic maze walls via GameLogic/MazeGenerator.
Push rows/cols via INIT instead of fixed 21x21.
Vite proxy for /ws to avoid env variables in dev.
Add auth and per-session player scoping in the DB model for >2 players.
