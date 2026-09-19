namespace ProtoTest.SampleApp.Testing;

using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Rest;

/// <summary>
/// Verifies once per run that the application exposes <c>/test-support</c>, so a deployment that
/// does not enable the development affordance fails with a clear message instead of a 404 during
/// scenario provisioning.
/// </summary>
internal static class TestSupportProbe
{
    // The run's own configuration is the identity of the run: a new host builds a new configuration,
    // so a second run in the same process probes again instead of trusting the first run's target.
    private static readonly ConditionalWeakTable<IConfiguration, StrongBox<bool>> Verified = new();

    public static async Task EnsureAvailableAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var verified = Verified.GetValue(context.Configuration, _ => new StrongBox<bool>());
        if (verified.Value)
        {
            return;
        }

        using var response = await context.Rest().WithoutAuth().GetAsync("/test-support");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "The application does not expose /test-support, so scenario provisioning is unavailable. " +
                "Run it with ProtoTest:TestSupport=true (a development affordance), or point " +
                "the suite at an environment that enables it.");
        }

        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        verified.Value = true;
    }
}
