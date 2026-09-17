namespace ProtoTest.Core;

/// <summary>
/// The in-process transport (an ASP.NET Core server client) backing an application. An application's
/// HTTP clients with no configured base address reuse this transport instead of opening a socket.
/// </summary>
public sealed record ProtoApplicationTransport(string ApplicationName, string ClientName);
