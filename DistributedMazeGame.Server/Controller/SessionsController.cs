using Microsoft.AspNetCore.Mvc;
using DistributedMazeGame.Server.Networking;

namespace DistributedMazeGame.Server.Controller
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class SessionsController : ControllerBase
    {
        private readonly WebSocketSessionManager _sessions;
        public SessionsController(WebSocketSessionManager sessions)
        {
            _sessions = sessions;
        }

        // GET /api/sessions
        [HttpGet]
        public IActionResult GetSessions()
        {
            var snapshot = _sessions.GetSessionsSnapshot();
            return Ok(snapshot);
        }

        // GET /api/sessions/{sessionId}
        [HttpGet("{sessionId}")]
        public IActionResult GetSession(string sessionId)
        {
            var match = _sessions.GetSessionsSnapshot().FirstOrDefault(s => s.SessionId == sessionId);
            if (match is null) return NotFound();
            return Ok(match);
        }
    }
}
