namespace ProtoTest.Demo;

using System.Globalization;
using System.Net;
using global::NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Messaging;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;
using ProtoTest.SampleApp.Testing;
using ProtoTest.Web;

/// <summary>
/// The application publishes <c>invoice.paid</c> when an invoice is paid; the test awaits that event on
/// its own tap queue. Without a configured broker the capability is absent and the journey skips, so the
/// suite stays honest in every environment.
/// </summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant(PlanIds.Growth)]
[Auth<NorthstarAuthenticator>]
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "No broker is configured; set ProtoTest:Messaging:RabbitMq:ConnectionString.")]
public sealed class MessagingJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task PayingAnInvoicePublishesAnInvoicePaidEvent()
    {
        // Arrange: an open invoice.
        var invoice = await DemoSupport.IssueInvoiceAsync();

        // Act: pay it over the API.
        using var paid = await Proto.Context.Rest()
            .Body(new PayInvoiceRequest(PaymentMethods.Visa))
            .PostAsync("/api/v1/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });
        paid.Should.HaveHttpStatus(HttpStatusCode.OK);

        // Assert: the application's event arrives, and it is about this invoice.
        var message = await Proto.Context.Messaging().AwaitAsync(
            "invoice.paid",
            candidate => candidate.Payload is not null
                && candidate.Payload.Contains(
                    invoice.Id.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal),
            TimeSpan.FromSeconds(15));

        Assert.Multiple(() =>
        {
            Assert.That(message.ContentType, Is.EqualTo("application/json"));
            Assert.That(message.Payload, Does.Contain($"\"status\":\"{InvoiceStatuses.Paid}\""));
        });
    }

    [ProtoTest]
    [SignedInAs]
    [LoginAs<NorthstarConsoleLogin>("owner")]
    [RequiresCapability(
        ProtoCapabilityKinds.Server,
        CapabilityName = "Northstar standalone",
        Reason = "Paying through the console needs the standalone application.")]
    [RequiresConsoleBuild]
    public async Task PayingInTheConsolePublishesAnInvoicePaidEvent()
    {
        // Arrange: an open invoice the console can pay.
        var invoice = await DemoSupport.IssueInvoiceAsync();

        // Act: pay it on the console's billing screen.
        var billing = Proto.Context.Web().Page<BillingPage>();
        await billing.OpenAsync("/console/billing");
        var row = billing.Invoice(invoice.Number);
        await row.Status.Should.HaveTextAsync("Open", DemoSupport.ConsoleWait);
        await row.Pay.ClickAsync();
        await row.Status.Should.HaveTextAsync("Paid", DemoSupport.ConsoleWait);

        // Assert: the browser payment published the event the test awaits on the broker.
        var message = await Proto.Context.Messaging().AwaitAsync(
            "invoice.paid",
            candidate => candidate.Payload is not null
                && candidate.Payload.Contains(
                    invoice.Id.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal),
            TimeSpan.FromSeconds(15));

        Assert.Multiple(() =>
        {
            Assert.That(message.ContentType, Is.EqualTo("application/json"));
            Assert.That(message.Payload, Does.Contain($"\"status\":\"{InvoiceStatuses.Paid}\""));
        });
    }
}
