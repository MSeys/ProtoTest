using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using ModelContextProtocol.AspNetCore;
using ProtoTest.Mcp;

// The demo-only MCP endpoint: the same read-only tools as the local `prototest-mcp` server, over the
// one bundled demo trace. No accounts, no uploads, no retention, no filesystem input from a caller -
// a tool error is a named error, never a fallback to another trace. The local stdio server stays the
// product surface; this project exists so the endpoint can be hosted later.
var builder = WebApplication.CreateBuilder(args);

// Loopback only, and the accepted Host names are the two loopback spellings.
builder.WebHost.UseUrls("http://127.0.0.1:5199");
builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1";

var demoTrace = Path.Combine(AppContext.BaseDirectory, "demo", "prototest-demo.prototrace");
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .AddProtoTestMcp(ProtoTestMcpOptions.ForTrace(demoTrace));

// A per-caller fixed window; stateless mode keeps no per-session state to retain.
const string RateLimitPolicy = "mcp-read-only";
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();
app.UseRateLimiter();
app.MapMcp().RequireRateLimiting(RateLimitPolicy);
app.Run();
