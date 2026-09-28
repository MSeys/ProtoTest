using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// A failure path the suite controls: setting Aspire:Test:FailStart makes the entry point throw
// before any resource is added, so the adapter's start failure names the AppHost.
if (builder.Configuration.GetValue<bool>("Aspire:Test:FailStart"))
{
    throw new InvalidOperationException("The test AppHost was told to fail at start.");
}

builder.AddProject<Projects.ProtoTest_Aspire_TestService>("api");

// The partial-configuration journey declares a second resource; other journeys start one service.
if (builder.Configuration.GetValue<bool>("Aspire:Test:TwoResources"))
{
    builder.AddProject<Projects.ProtoTest_Aspire_TestService>("api2");
}

// The connection-string journey declares a resource without an endpoint, so a mapped key resolves
// the AppHost's own value instead of an application address.
if (builder.Configuration.GetValue<bool>("Aspire:Test:ConnectionString"))
{
    builder.AddConnectionString("db", reference => reference.Append($"Host=apphost"));
}

// The settings-bridge journeys: the run forwards its configuration and the settings earlier
// infrastructure published to the AppHost, which reads them back into the graph. A suite sets the
// echo key in its configuration or publishes it from a settings piece.
if (builder.Configuration.GetValue<string>("Aspire:Test:Echo") is { Length: > 0 } echo)
{
    builder.AddConnectionString("echo", reference => reference.Append($"{echo}"));
}

if (builder.Configuration.GetValue<string>("Aspire:Test:SettingsEcho") is { Length: > 0 } settingsEcho)
{
    builder.AddConnectionString("settings-echo", reference => reference.Append($"{settingsEcho}"));
}

builder.Build().Run();
