namespace ProtoTest.SampleApp.Testing;

using System.Net;
using ProtoTest.Core;
using ProtoTest.Rest;

/// <summary>
/// Verifies once per process that the application exposes <c>/test-support</c>, so a deployment that
/// does not enable the development affordance fails with a clear message instead of a 404 during
/// scenario provisioning.
/// </summary>
internal static class TestSupportProbe
{
    private static int _verified;

    public static async Task EnsureAvailableAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Volatile.Read(ref _verified) == 1)
        {
            return;
        }

        using var response = await context.Rest().WithoutAuth().GetAsync("/test-support");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "The application does not expose /test-support, so scenario provisioning is unavailable. " +
                "Start the application with PROTOTEST_TEST_SUPPORT=1 (a development affordance), or point " +
                "the suite at an environment that enables it.");
        }

        response.ShouldHaveHttpStatus(HttpStatusCode.OK);
        Volatile.Write(ref _verified, 1);
    }
}
