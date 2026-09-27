namespace ProtoTest.WireMock.Internal;

using ProtoTest.Core;

/// <summary>
/// Reports each fake the test used after the test completes: new requests become trace observations.
/// Per-test fakes need no reset — their server stops with the test's resources right after this hook;
/// a per-run fake keeps its stubs and log until the run releases it, so an explicit
/// <c>Reset()</c> is the way to clear shared state mid-run.
/// </summary>
internal sealed class ProtoWireMockTestHook : IProtoTestHook
{
    public async Task AfterTestAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var registry = context.TryService<ProtoWireMockRegistry>();
        if (registry is null)
        {
            return;
        }

        foreach (var name in registry.Names)
        {
            var client = context.TryClient<ProtoWireMockClient>(name);
            if (client is null)
            {
                continue;
            }

            client.Session.ReportNewEntries(context);
        }

        await Task.CompletedTask;
    }
}
