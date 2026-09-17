namespace ProtoTest.Core;

/// <summary>The ordered clients registered for one application, per protocol.</summary>
public sealed record ProtoApplicationClients(string ApplicationName, IReadOnlyList<ProtoApplicationClient> Clients);
