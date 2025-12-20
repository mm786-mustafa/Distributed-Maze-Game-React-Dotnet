// Networking/Contracts.cs
public record PlayerInput(int PlayerId, int Seq, string Direction, DateTime ClientTime);
public record AcceptedMove(int SessionId, int PlayerId, int Seq, string Direction, DateTime ServerTime);
