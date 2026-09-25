namespace ProtoTest.AspNetCore.SecondTestApi;

/// <summary>
/// A second in-process application for multi-application tests (audit DEV-1): it exposes the same
/// WebSocket path as <c>ProtoTest.AspNetCore.SampleApi.Program</c> with a distinguishable echo, so a
/// test can prove which application a device client reached instead of only that a path matched.
/// </summary>
public sealed class Program
{
    /// <summary>The prefix its WebSocket echo adds, so the second application is recognisable.</summary>
    public const string EchoPrefix = "SECOND:";

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.UseWebSockets();
        app.Map("/ws/{deviceId}", async context =>
        {
            var socket = await context.WebSockets.AcceptWebSocketAsync();
            try
            {
                var buffer = new byte[4096];
                while (socket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    {
                        return;
                    }

                    var text = System.Text.Encoding.UTF8.GetString(buffer.AsSpan(0, result.Count).ToArray());
                    var response = $"{EchoPrefix}{text}_ACK";
                    await socket.SendAsync(
                        System.Text.Encoding.UTF8.GetBytes(response),
                        System.Net.WebSockets.WebSocketMessageType.Text,
                        endOfMessage: true,
                        CancellationToken.None);
                }
            }
            finally
            {
                socket.Dispose();
            }
        });
        app.Run();
    }
}
