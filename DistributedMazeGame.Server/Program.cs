using DistributedMazeGame.Server.Data;
using DistributedMazeGame.Server.Services;
using DistributedMazeGame.Server.Networking;
using System.Threading.Channels; 
using DistributedMazeGame.Server.GameLogic;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// --------------------
// Environment-based config
// --------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// --------------------
// Services (DI)
// --------------------
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
{
    // Avoid AutoDetect (which connects at startup). Use explicit server version.
    var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
    options.UseMySql(connectionString, serverVersion,
        mySql => mySql.EnableRetryOnFailure());
});

builder.Services.AddSingleton<GameAuthoritativeService>(); // applies moves & returns state payloads
builder.Services.AddSingleton<WebSocketSessionManager>();  // manages sessions

builder.Services.AddControllers();

var app = builder.Build();

// --------------------
// Middleware pipeline
// --------------------
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();

// Enable WebSockets
var webSocketOptions = new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(120)
};
app.UseWebSockets(webSocketOptions);

app.MapControllers();

// Route: /ws?sessionId=123 
app.Map("/ws", async (HttpContext ctx, WebSocketSessionManager sessions) => 
{ 
    if (!ctx.WebSockets.IsWebSocketRequest) 
    { 
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest; 
        await ctx.Response.WriteAsync("WebSocket request required."); 
        return; 
    } 
    
    var sessionId = ctx.Request.Query["sessionId"].ToString(); 
    if (string.IsNullOrWhiteSpace(sessionId)) 
    { 
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest; 
        await ctx.Response.WriteAsync("sessionId is required."); 
        return; 
    } 
    
    using var socket = await ctx.WebSockets.AcceptWebSocketAsync(); 
    await sessions.HandleClientAsync(sessionId, socket, ctx.RequestAborted); 
});

app.Run();
