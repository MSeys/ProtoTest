namespace ProtoTest.Demo;

using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;
using System.Net;

/// <summary>Usage becomes an invoice, the invoice is paid (or declines), and the plan is changed.</summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant(PlanIds.Growth)]
[Auth<NorthstarAuthenticator>]
public sealed class BillingJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task UsageIsMeteredAgainstThePlanAllowance()
    {
        // Arrange
        using var recorded = await Proto.Context.Rest()
            .Body(new RecordUsageRequest(UsageMetrics.DeployMinutes, 100_500))
            .PostAsync("/api/v1/usage");
        recorded.Should.HaveHttpStatus(HttpStatusCode.Created);

        // Act
        using var summary = await Proto.Context.Rest()
            .GetAsync("/api/v1/usage/summary", new { metric = UsageMetrics.DeployMinutes });

        // Assert
        summary.Should.HaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            metric = UsageMetrics.DeployMinutes,
            total = 100_500d,
            included = 100_000,
            overage = 500d
        });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task DeployMinuteOverageIsInvoicedAtPeriodClose()
    {
        // Arrange
        using var recorded = await Proto.Context.Rest()
            .Body(new RecordUsageRequest(UsageMetrics.DeployMinutes, 100_500))
            .PostAsync("/api/v1/usage");
        recorded.Should.HaveHttpStatus(HttpStatusCode.Created);

        // Act
        await DemoSupport.AdvanceClockAsync(31);

        // Assert
        using var invoices = await Proto.Context.Rest()
            .GetAsync("/api/v1/invoices", new { status = InvoiceStatuses.Open });
        invoices.Should.HaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            items = new[]
            {
                new
                {
                    status = InvoiceStatuses.Open,
                    subtotal = 200.00m,
                    tax = 40.00m,
                    total = 240.00m,
                    lines = new[]
                    {
                        new { description = (object)"Northstar Growth plan", amount = 199.00m },
                        new { description = (object)JsonValue.StringContaining("Deploy-minute overage"), amount = 1.00m }
                    }
                }
            }
        });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task PayingAnInvoiceEmitsASignedInvoicePaidWebhook()
    {
        // Arrange
        var sink = await DemoSupport.CreateSinkAsync(0);
        using var webhook = await Proto.Context.Rest()
            .Body(new CreateWebhookRequest(sink.Url.ToString(), [WebhookEventTypes.InvoicePaid]))
            .PostAsync("/api/v1/webhooks");
        webhook.Should.HaveHttpStatus(HttpStatusCode.Created);
        var endpoint = webhook.ReadAsJson<WebhookEndpointResponse>()!;
        var invoice = await DemoSupport.IssueInvoiceAsync();

        // Act
        using var paid = await Proto.Context.Rest()
            .Body(new PayInvoiceRequest(PaymentMethods.Visa))
            .PostAsync("/api/v1/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });

        // Assert
        paid.Should.HaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            status = InvoiceStatuses.Paid,
            paidAtUtc = JsonValue.NotNull(),
            payments = new[] { new { status = PaymentStatuses.Succeeded, method = PaymentMethods.Visa } }
        });
        var delivery = await DemoSupport.WaitForDeliveredAsync(WebhookEventTypes.InvoicePaid);
        var receipt = (await DemoSupport.ReceiptsAsync(sink.Id)).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(delivery.Attempts, Is.EqualTo(1));
            Assert.That(receipt.Signature, Is.EqualTo(DemoSupport.Sign(endpoint.Secret, receipt.Body)));
        }
    }

    [ProtoTest]
    [SignedInAs]
    public async Task ADeclinedPaymentLeavesTheInvoiceOpenAndFailsThePayment()
    {
        // Arrange
        var invoice = await DemoSupport.IssueInvoiceAsync();

        // Act
        using var declined = await Proto.Context.Rest()
            .Body(new PayInvoiceRequest(PaymentMethods.Declined))
            .PostAsync("/api/v1/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });

        // Assert
        declined.Should.HaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            status = InvoiceStatuses.Open,
            paidAtUtc = JsonValue.Null(),
            payments = new[]
            {
                new { status = PaymentStatuses.Failed, failureReason = "card_declined" }
            }
        });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task APastDueSubscriptionBlocksNewProjects()
    {
        // Arrange
        var invoice = await DemoSupport.IssueInvoiceAsync();
        using var declined = await Proto.Context.Rest()
            .Body(new PayInvoiceRequest(PaymentMethods.Declined))
            .PostAsync("/api/v1/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });
        declined.Should.HaveHttpStatus(HttpStatusCode.OK);
        using var pastDue = await Proto.Context.Rest().GetAsync("/api/v1/organization");
        pastDue.Should.HaveHttpStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new { status = SubscriptionStatuses.PastDue });

        // Act
        using var project = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("blocked"))
            .PostAsync("/api/v1/projects");

        // Assert
        project.Should.HaveHttpStatus(HttpStatusCode.PaymentRequired)
            .ShouldMatchShape(new { code = ProblemCodes.PaymentRequired });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task PayingAfterADeclineRestoresAccess()
    {
        // Arrange
        var invoice = await DemoSupport.IssueInvoiceAsync();
        using var declined = await Proto.Context.Rest()
            .Body(new PayInvoiceRequest(PaymentMethods.Declined))
            .PostAsync("/api/v1/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });
        declined.Should.HaveHttpStatus(HttpStatusCode.OK);

        // Act
        using var recovered = await Proto.Context.Rest()
            .Body(new PayInvoiceRequest(PaymentMethods.Visa))
            .PostAsync("/api/v1/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });

        // Assert
        recovered.Should.HaveHttpStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new { status = InvoiceStatuses.Paid });
        using var project = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("recovered"))
            .PostAsync("/api/v1/projects");
        project.Should.HaveHttpStatus(HttpStatusCode.Created);
    }

    [ProtoTest]
    [SignedInAs]
    public async Task UpgradingMidCycleChangesThePlanImmediately()
    {
        // Arrange
        await DemoSupport.AdvanceClockAsync(15);

        // Act
        using var upgraded = await Proto.Context.Rest()
            .Body(new ChangePlanRequest(PlanIds.Enterprise, 10))
            .PostAsync("/api/v1/subscription");

        // Assert
        upgraded.Should.HaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            planId = PlanIds.Enterprise,
            planName = "Enterprise",
            monthlyBasePrice = 499m
        });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task TheNextInvoiceCreditsTheUnusedPortionOfThePreviousPlan()
    {
        // Arrange
        await DemoSupport.AdvanceClockAsync(15);
        using var upgraded = await Proto.Context.Rest()
            .Body(new ChangePlanRequest(PlanIds.Enterprise, 10))
            .PostAsync("/api/v1/subscription");
        upgraded.Should.HaveHttpStatus(HttpStatusCode.OK);

        // Act
        await DemoSupport.AdvanceClockAsync(16);

        // Assert
        using var invoices = await Proto.Context.Rest().GetAsync("/api/v1/invoices");
        var invoice = invoices.ReadAsJson<CursorPage<InvoiceResponse>>()!.Items.Single();
        var credit = invoice.Lines.SingleOrDefault(line => line.Description == "Account credit");
        Assert.That(credit?.Amount, Is.LessThan(0m));
    }

    [ProtoTest]
    [SignedInAs]
    public async Task CancelingSchedulesTheSubscriptionToEndAtPeriodClose()
    {
        // Act
        using var canceled = await Proto.Context.Rest().PostAsync("/api/v1/subscription/cancel");

        // Assert
        canceled.Should.HaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            cancelAtPeriodEnd = true,
            status = SubscriptionStatuses.Active
        });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task ACanceledSubscriptionBlocksNewProjects()
    {
        // Arrange
        using var canceled = await Proto.Context.Rest().PostAsync("/api/v1/subscription/cancel");
        canceled.Should.HaveHttpStatus(HttpStatusCode.OK);
        await DemoSupport.AdvanceClockAsync(31);

        // Act
        using var project = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("too-late"))
            .PostAsync("/api/v1/projects");

        // Assert
        project.Should.HaveHttpStatus(HttpStatusCode.PaymentRequired)
            .ShouldMatchShape(new { code = ProblemCodes.PaymentRequired });
        using var subscription = await Proto.Context.Rest().GetAsync("/api/v1/subscription");
        subscription.Should.HaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            status = SubscriptionStatuses.Canceled,
            cancelAtPeriodEnd = false
        });
    }
}
