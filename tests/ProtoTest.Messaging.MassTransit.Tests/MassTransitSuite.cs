namespace ProtoTest.Messaging.MassTransit.Tests;

using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.MassTransit.TestApi;

/// <summary>
/// The suite's shared composition: the application under test hosted in-process, and the messaging
/// adapter registered after it - the order the bridge's harness resolution needs, because the messaging
/// client initializes once the server exists. The default registration pre-binds the destinations the
/// tests await, so each test's consumer snapshots the harness during setup and an act-then-await flow
/// cannot miss a message the act published.
/// </summary>
internal static class MassTransitSuite
{
    /// <summary>The application name the server and the bridge are registered under.</summary>
    public const string Application = "Default";

    /// <summary>The message contract types the suite's default registration pre-binds.</summary>
    public static readonly string[] Destinations = [nameof(PaymentReceived), nameof(InvoicePaid)];

    public static ProtoHostBuilder Builder(
        Action<ProtoMessagingBuilder>? messaging = null,
        bool trace = false)
    {
        var builder = new ProtoHostBuilder();
        if (!trace)
        {
            builder.ConfigureTracing(options => options.Enabled = false);
        }

        builder.AddAspNetCoreServer<Program>(Application);
        builder.AddMessaging(messaging ?? (m => m.Tap(Destinations).UseMassTransit<Program>(Application)));
        return builder;
    }
}
