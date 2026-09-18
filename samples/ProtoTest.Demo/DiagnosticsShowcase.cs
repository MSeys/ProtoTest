namespace ProtoTest.Demo;

using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;
using System.Net;

/// <summary>
/// ProtoTest's own diagnostics: captured shape mismatches, recorded failures and the opt-in
/// intentional failure used to keep the trace viewer demo complete.
/// </summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant]
[Auth<NorthstarAuthenticator>]
public sealed class DiagnosticsShowcase
{
    [ProtoTest]
    [SignedInAs]
    public async Task ShapeMismatchesAreCapturedWithoutFailingTheRun()
    {
        // Arrange
        using var organization = await Proto.Context.Rest().GetAsync("/api/v1/organization");
        organization.ShouldHaveHttpStatus(HttpStatusCode.OK);

        // Act
        JsonShapeMismatchException? mismatch = null;
        try
        {
            organization.ShouldMatchShape(new
            {
                projectCount = 99,
                planThatDoesNotExist = "enterprise"
            });
        }
        catch (JsonShapeMismatchException exception)
        {
            mismatch = exception;
        }

        // Assert
        Assert.That(mismatch, Is.Not.Null);
        Proto.Context.RecordObservation(
            "TraceViewer",
            "failure.shape.captured",
            Proto.Context.TestId,
            new { mismatch!.Message, MismatchCount = mismatch.Mismatches.Count });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task AFailedOperationRecordsItsDiagnosticsAndTheRunContinues()
    {
        // Arrange
        var exception = new TimeoutException(
            "The billing ledger did not acknowledge the webhook within 2 seconds.");

        // Act
        using var operation = Proto.Context.Trace
            .Operation("northstar.webhook.deliver", "Deliver subscription webhook", "ProtoTest.Demo")
            .With("webhook.destination", "billing-ledger")
            .With("webhook.attempt", "3")
            .Begin();
        operation.Fail(exception);

        // Assert
        Proto.Context.RecordObservation(
            "TraceViewer",
            "failure.timeout.captured",
            Proto.Context.TestId,
            new { exception.Message });
        using var organization = await Proto.Context.Rest().GetAsync("/api/v1/organization");
        organization.ShouldHaveHttpStatus(HttpStatusCode.OK);
    }

    [ProtoTest]
    [SignedInAs]
    public async Task FindingsReachTheReportWithoutFailingTheRun()
    {
        // Arrange
        var project = await DemoSupport.CreateProjectAsync("ledger");
        var production = await DemoSupport.CreateEnvironmentAsync(project.Id, "production", EnvironmentKinds.Production);

        // Act
        var deployment = await DemoSupport.DeployAsync(production.Id, "1.0.0", "abc1234");

        // Assert: a finding is evidence, not a failure - the test still passes and the report shows it.
        Proto.Context.AddFinding(
            $"Deployment {deployment.Version} took {deployment.DeployMinutes:0.#} deploy-minutes.",
            ProtoReportStatus.Warning,
            category: "Delivery",
            tags: ["delivery-budget"]);
        Assert.That(deployment.Status, Is.EqualTo(DeploymentStatuses.Succeeded));
    }

    /// <summary>
    /// The demo's one intentional failure: the expected plan and project count are deliberately wrong, so the
    /// viewer demo always has a failed shape check to show. Opt-in, so an ordinary run stays green.
    /// </summary>
    [ProtoTest]
    [SignedInAs]
    public async Task TheOrganizationReportsItsPlanAndProjectCount()
    {
        // Arrange
        if (!string.Equals(
                Environment.GetEnvironmentVariable("PROTOTEST_DEMO_INCLUDE_FAILURE"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Ignore("Set PROTOTEST_DEMO_INCLUDE_FAILURE=1 to include the intentional viewer-demo failure.");
        }

        // Act
        using var organization = await Proto.Context.Rest().GetAsync("/api/v1/organization");

        // Assert
        organization.ShouldHaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            projectCount = 99,
            planId = "nonexistent-plan"
        });
    }
}
