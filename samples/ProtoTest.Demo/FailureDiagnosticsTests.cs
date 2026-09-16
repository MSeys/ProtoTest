namespace ProtoTest.Demo;

using System.Net;
using ProtoTest.Core;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;

[RestClient(SampleAppTargets.Api)]
[SampleEnvironment]
[Auth<SampleUserAuthenticator>]
public sealed class FailureDiagnosticsTests
{
    [ProtoTest]
    [SampleUser(SampleRoles.TenantAdministrator)]
    public async Task CapturedShapeMismatchKeepsPropertyDiagnosticsInSuccessfulRun()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/control-plane");
        response.ShouldHaveStatus(HttpStatusCode.OK);

        try
        {
            response.ShouldMatchShape(new
            {
                workspaceCount = 99,
                releaseCount = 42,
                planThatDoesNotExist = "enterprise"
            });
            Assert.Fail("The diagnostic mismatch was expected.");
        }
        catch (JsonShapeMismatchException exception)
        {
            Proto.Context.RecordObservation(
                "TraceViewer",
                "failure.shape.captured",
                Proto.Context.TestId,
                new { exception.Message, MismatchCount = exception.Mismatches.Count });
        }
    }

    [ProtoTest]
    [SampleUser]
    public void CapturedDependencyTimeoutShowsErrorAndStackWithoutFailingBuild()
    {
        using var operation = Proto.Context.Trace.StartOperation(
            "saas.webhook.deliver",
            "Deliver subscription webhook",
            "ProtoTest.Demo",
            attributes: new Dictionary<string, string?>
            {
                ["webhook.destination"] = "billing-ledger",
                ["webhook.attempt"] = "3"
            });
        var exception = new TimeoutException("The billing ledger did not acknowledge the webhook within 2 seconds.");
        operation.Fail(exception);
        Proto.Context.RecordObservation(
            "TraceViewer",
            "failure.timeout.captured",
            Proto.Context.TestId,
            new { exception.Message });
    }

    [ProtoTest]
    [SampleUser(SampleRoles.TenantAdministrator)]
    public async Task IntentionalFailureShowsFailedTestShapeMismatchAndTeardown()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("PROTOTEST_DEMO_INCLUDE_FAILURE"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Ignore("Set PROTOTEST_DEMO_INCLUDE_FAILURE=1 to include the intentional viewer-demo failure.");
        }

        using var response = await Proto.Context.Rest().GetAsync("/api/control-plane");
        response.ShouldHaveStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            workspaceCount = 99,
            releaseCount = 42,
            monthlyRecurringRevenue = 9999m
        });
    }
}
