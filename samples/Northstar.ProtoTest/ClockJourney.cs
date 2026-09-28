namespace Northstar.ProtoTest;

using System.Net;
using global::NUnit.Framework;
using global::ProtoTest.Core;
using global::ProtoTest.Http;
using global::ProtoTest.NUnit;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>
/// The determinism journey: the application computes every stamp and window from <c>TimeProvider</c>,
/// and the in-process server hands it the test clock, so moving that clock is what closes a billing
/// period. Nothing here waits for real time.
/// </summary>
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
public sealed class ClockJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task TheApplicationStampsWritesFromTheTestClock()
    {
        var now = Proto.Context.Clock.GetUtcNow();
        var name = $"clock-{Proto.Context.TestId}";
        using var created = await Proto.Context.Rest()
            .Body(new CreateProjectRequest(name))
            .PostAsync("/api/v1/projects");

        var project = created
            .Should.HaveHttpStatus(HttpStatusCode.Created)
            .ReadRequired<ProjectResponse>();
        Assert.That(
            project.CreatedAtUtc,
            Is.EqualTo(now),
            "the application stamped the project from the test clock, not from real time");
    }

    [ProtoTest]
    [SignedInAs]
    public async Task ClosingTheBillingPeriodIssuesTheInvoiceOnTheTestClock()
    {
        using var subscription = await Proto.Context.Rest().GetAsync("/api/v1/subscription");
        var current = subscription
            .Should.HaveHttpStatus(HttpStatusCode.OK)
            .ReadRequired<SubscriptionResponse>();

        // Move the test clock past the period end; the application reads the same clock.
        Proto.Context.Clock.Advance(
            current.CurrentPeriodEndUtc - Proto.Context.Clock.GetUtcNow() + TimeSpan.FromSeconds(1));

        using var invoices = await Proto.Context.Rest()
            .GetAsync("/api/v1/invoices", new { status = InvoiceStatuses.Open });
        var invoice = invoices
            .Should.HaveHttpStatus(HttpStatusCode.OK)
            .ReadRequired<CursorPage<InvoiceResponse>>()
            .Items
            .Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                invoice.IssuedAtUtc,
                Is.EqualTo(current.CurrentPeriodEndUtc),
                "the application closed the period at the end it computed from the test clock");
            Assert.That(invoice.Status, Is.EqualTo(InvoiceStatuses.Open));
            Assert.That(
                invoice.Lines,
                Has.Some.Property(nameof(InvoiceLineResponse.Description)).Contains("Growth"));
        }
    }
}
