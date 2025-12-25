# Distributed Maze Game - PDC Project Documentation

## 🎮 Project Overview

A real-time multiplayer maze game demonstrating Parallel and Distributed Computing (PDC) concepts. Players compete to capture flags in a procedurally generated maze, with game state synchronized across all clients through a central authoritative server.

---

## 📐 System Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         CLIENTS                                  │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐        │
│  │ Player 1 │  │ Player 2 │  │ Player 3 │  │ Player 4 │        │
│  │  (React) │  │  (React) │  │  (React) │  │  (React) │        │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘        │
│       │             │             │             │                │
│       └─────────────┴──────┬──────┴─────────────┘                │
│                            │                                     │
│                     WebSocket + REST                             │
│                            │                                     │
└────────────────────────────┼─────────────────────────────────────┘
                             │
┌────────────────────────────┼─────────────────────────────────────┐
│                     GAME SERVER                                  │
│                            │                                     │
│  ┌─────────────────────────▼───────────────────────────┐        │
│  │           WebSocket Session Manager                  │        │
│  │  - Handles concurrent client connections             │        │
│  │  - Routes messages to game sessions                  │        │
│  │  - Broadcasts state updates (fan-out pattern)        │        │
│  └─────────────────────────┬───────────────────────────┘        │
│                            │                                     │
│  ┌─────────────────────────▼───────────────────────────┐        │
│  │          Game Authoritative Service                  │        │
│  │  - Single source of truth (centralized authority)    │        │
│  │  - Race condition prevention (mutex locks)           │        │
│  │  - Concurrent data structures (ConcurrentDictionary) │        │
│  │  - Flag capture synchronization                      │        │
│  └─────────────────────────┬───────────────────────────┘        │
│                            │                                     │
│  ┌─────────────────────────▼───────────────────────────┐        │
│  │              Leaderboard Controller                  │        │
│  │  - REST API for daily/all-time leaderboards          │        │
│  │  - Player statistics and history                     │        │
│  └─────────────────────────┬───────────────────────────┘        │
│                            │                                     │
└────────────────────────────┼─────────────────────────────────────┘
                             │
┌────────────────────────────┼─────────────────────────────────────┐
│                      DATABASE                                    │
│  ┌─────────────────────────▼───────────────────────────┐        │
│  │                MySQL Database                        │        │
│  │  - Players, GameSessions, Moves tables               │        │
│  │  - PlayerScores (per-game results)                   │        │
│  │  - DailyWins (pre-aggregated leaderboard)            │        │
│  │  - Optimized indexes for queries                     │        │
│  └─────────────────────────────────────────────────────┘        │
└──────────────────────────────────────────────────────────────────┘
```

---

## 🔧 PDC Concepts Demonstrated

### 1. **Centralized Authority Model**
- **Location**: `GameAuthoritativeService.cs`
- **Concept**: Server is the single source of truth for game state
- **Benefits**:
  - Prevents cheating (all moves validated server-side)
  - Eliminates state conflicts
  - Ensures consistency across distributed clients

### 2. **Race Condition Prevention**
- **Location**: `GameAuthoritativeService.ApplyMoveAsync()`
- **Concept**: Multiple players may reach the flag simultaneously
- **Solution**: Mutex lock around flag capture check
```csharp
lock (s.FlagLock)
{
    // Only one player can capture flag even if they arrive at same tick
    if (nx == s.Flag.x && ny == s.Flag.y && !s.Completed)
    {
        s.Scores.AddOrUpdate(playerId, 1, (_, old) => old + 1);
        capturedBy = playerId;
        // ... spawn new flag
    }
}
```

### 3. **Concurrent Data Structures**
- **Location**: `GameAuthoritativeService.State` class
- **Concept**: Thread-safe collections for multi-threaded access
```csharp
ConcurrentDictionary<int, (int x, int y)> Positions = new();
ConcurrentDictionary<int, int> Scores = new();
ConcurrentDictionary<int, string> PlayerNames = new();
```

### 4. **Event-Driven Architecture**
- **Location**: `WebSocketSessionManager.cs`
- **Concept**: Asynchronous message processing
- **Pattern**: Publisher-Subscriber for game events

### 5. **Broadcast Pattern (Fan-Out)**
- **Location**: `GameSession.BroadcastAsync()`
- **Concept**: Efficiently send state to all connected clients
```csharp
var tasks = targets.Select(p => SendSafe(p.Socket, bytes, ct));
await Task.WhenAll(tasks); // Parallel send to all clients
```

### 6. **Database Transaction for Consistency**
- **Location**: `GameAuthoritativeService.PersistGameResultsAsync()`
- **Concept**: ACID transaction for game results
```csharp
await using var transaction = await db.Database.BeginTransactionAsync(ct);
try {
    // Save player scores, update daily wins, create result
    await transaction.CommitAsync(ct);
} catch {
    await transaction.RollbackAsync(ct);
    throw;
}
```

### 7. **Pre-Aggregated Data (CQRS Pattern)**
- **Location**: `DailyWin` entity
- **Concept**: Separate read and write models for efficiency
- **Benefit**: Daily leaderboard queries are O(1) instead of expensive GROUP BY

### 8. **Connection Pooling**
- **Location**: `IDbContextFactory<ApplicationDbContext>`
- **Concept**: Efficient database connection management
- **Benefit**: Parallel database operations without connection contention

---

## 📊 Database Schema

### Entity Relationship Diagram

```
┌─────────────┐       ┌─────────────────┐       ┌──────────────┐
│   Player    │       │   GameSession   │       │    Result    │
├─────────────┤       ├─────────────────┤       ├──────────────┤
│ PlayerId PK │◄──────│ SessionId PK    │──────►│ ResultId PK  │
│ Name        │       │ StartTime       │       │ SessionId FK │
│ ConnectedAt │       │ EndTime         │       │ WinnerPId FK │
└─────┬───────┘       │ Status          │       │ Duration     │
      │               │ TotalFlags      │       └──────────────┘
      │               │ PlayerCount     │
      │               └────────┬────────┘
      │                        │
      │    ┌───────────────────┼───────────────────┐
      │    │                   │                   │
      │    ▼                   ▼                   ▼
┌─────┴────────┐    ┌─────────────────┐    ┌─────────────┐
│    Move      │    │  PlayerScore    │    │  DailyWin   │
├──────────────┤    ├─────────────────┤    ├─────────────┤
│ MoveId PK    │    │ PlayerScoreId   │    │ DailyWinId  │
│ PlayerId FK  │    │ SessionId FK    │    │ PlayerId FK │
│ SessionId FK │    │ PlayerId FK     │    │ Date        │
│ Direction    │    │ PlayerName      │    │ WinCount    │
│ Timestamp    │    │ FlagsCaptured   │    │ TotalFlags  │
└──────────────┘    │ IsWinner        │    │ GamesPlayed │
                    │ FinalRank       │    │ LastUpdated │
                    │ RecordedAt      │    └─────────────┘
                    └─────────────────┘
```

### Key Design Decisions

1. **DailyWin Denormalization**: Pre-aggregated daily stats avoid expensive GROUP BY queries on every leaderboard request.

2. **PlayerScore History**: Stores individual game results for replay/analysis and historical accuracy.

3. **Composite Indexes**: Optimized for common queries:
   - `(Date, WinCount)` for daily leaderboard
   - `(PlayerId, Date)` unique for DailyWin
   - `(IsWinner, RecordedAt)` for finding winners

---

## 🔌 API Reference

### WebSocket Events

| Event | Direction | Description |
|-------|-----------|-------------|
| `ASSIGNED` | Server→Client | Player ID assigned on connection |
| `PLAYER_JOINED` | Server→Client | Another player joined the room |
| `PLAYER_LEFT` | Server→Client | A player disconnected |
| `SET_NAME` | Client→Server | Set player display name |
| `NAME_CHANGED` | Server→Client | Player changed their name |
| `INIT` | Server→Client | Game started, initial state |
| `MOVE_*` | Client→Server | Player movement (UP/DOWN/LEFT/RIGHT) |
| `STATE` | Server→Client | Updated game state after move |
| `FLAG_CAPTURED` | Server→Client | Player captured a flag |
| `GAME_OVER` | Server→Client | Game ended, final results |

### REST Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/leaderboard/daily` | GET | Today's top winners |
| `/api/leaderboard/daily/{date}` | GET | Leaderboard for specific date |
| `/api/leaderboard/alltime` | GET | All-time top winners |
| `/api/leaderboard/player/{id}` | GET | Player statistics |
| `/api/leaderboard/recent` | GET | Recent game results |
| `/api/sessions` | GET | Active game sessions |

---

## 🚀 Running the Project

### Prerequisites
- .NET 9.0 SDK
- Node.js 18+
- MySQL 8.0+

### Server Setup
```bash
cd DistributedMazeGame.Server

# Update connection string in appsettings.json
# Apply database migrations
dotnet ef database update

# Run the server
dotnet run
```

### Client Setup
```bash
cd DistributedMazeClient

# Install dependencies
npm install

# Development mode
npm run dev

# Production build
npm run build
```

### Environment Variables

**Server** (`appsettings.json`):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=localhost;port=3306;database=MazeGame;user=maze_user;password=YourPassword;"
  }
}
```

**Client** (`.env.development`):
```
VITE_BACKEND_URL=ws://localhost:5000/ws
VITE_API_URL=http://localhost:5000
```

---

## 🧪 Testing Distributed Scenarios

### Scenario 1: Race Condition Test
1. Open 2 browser windows
2. Join the same room
3. Both players reach the flag simultaneously
4. Verify only ONE player captures it (check server logs)

### Scenario 2: Scalability Test
1. Run multiple server instances (different ports)
2. Use a load balancer
3. Verify session affinity (sticky sessions) works

### Scenario 3: Reconnection Test
1. Start a game with 2 players
2. Disconnect one player (close browser)
3. Verify remaining player sees PLAYER_LEFT event
4. Reconnect and verify state recovery

---

## 📈 Performance Considerations

1. **Message Batching**: State updates are coalesced to reduce network traffic
2. **Indexed Queries**: All leaderboard queries use database indexes
3. **Connection Pooling**: DbContextFactory manages connections efficiently
4. **Parallel Broadcasting**: Task.WhenAll for concurrent client updates
5. **Pre-computed Aggregates**: DailyWin table avoids expensive runtime aggregation

---

## 📚 Technologies Used

- **Backend**: ASP.NET Core 9.0, Entity Framework Core, WebSockets
- **Frontend**: React 18, Vite, CSS3 3D Transforms
- **Database**: MySQL 8.0
- **Protocols**: WebSocket (real-time), REST (leaderboards)

---

## 👨‍🎓 Academic Evaluation Points

1. **Concurrency Control**: Mutex locks, ConcurrentDictionary, async/await
2. **Distributed State**: Client-server synchronization, authoritative model
3. **Scalability**: Stateless API design, connection pooling
4. **Fault Tolerance**: Reconnection handling, graceful degradation
5. **Data Consistency**: ACID transactions, atomic operations
6. **Performance Optimization**: Indexes, pre-aggregation, parallel I/O
