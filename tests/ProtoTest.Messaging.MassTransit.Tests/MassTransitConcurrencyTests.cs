namespace ProtoTest.Messaging.MassTransit.Tests;

using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.MassTransit.TestApi;
using ProtoTest.TestSupport;

/// <summary>
/// The MassTransit half of the shared concurrency contract (see
/// <see cref="MessagingConcurrencyContract"/>): the harness keeps its published history for the whole
/// run, so the consumer's position snapshot and the one-await-at-a-time loop are what make concurrent
/// awaits neither lose nor steal each other's messages.
/// </summary>
[TestFixture]
public sealed class MassTransitConcurrencyTests
{
    [Test]
    public async Task ConcurrentAwaitsOnOneDestination_ShouldNotLoseOrStealMessages()
    {
        await using var host = MassTransitSuite.Builder(m => m.UseMassTransit<Program>()).Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit concurrent contract", TestMethods.Placeholder);
        var broker = context.Service<IProtoMessageBroker>();

        await MessagingConcurrencyContract.ConcurrentAwaitsOnOneDestination_ShouldNotLoseOrStealMessages(
            broker,
            nameof(ContractProbe),
            strictPayload: """{"value":"strict"}""",
            lenientPayload: """{"value":"lenient"}""");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }
}

/// <summary>A test-local message contract: the concurrency contract needs a typed destination.</summary>
public sealed record ContractProbe(string Value);
