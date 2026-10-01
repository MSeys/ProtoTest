namespace Northstar.ProtoTest;

using System.Net;
using global::NUnit.Framework;
using global::ProtoTest.Core;
using global::ProtoTest.Data;
using global::ProtoTest.Http;
using global::ProtoTest.Messaging;
using global::ProtoTest.NUnit;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>
/// The broker journey: paying an invoice publishes the application's event, which the test awaits on
/// its own tap. Without a broker the capability is absent and the journey skips with a named reason.
/// </summary>
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
[RequiresCapability(ProtoCapabilityKinds.Broker)]
public sealed class BrokerJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task PayingAnInvoicePublishesAnInvoicePaidEvent()
    {
        var invoice = await Proto.Context.Data().IssueInvoiceAsync();

        using var paid = await Proto.Context.Rest()
            .Body(new PayInvoiceRequest(PaymentMethods.Visa))
            .PostAsync("/api/v1/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });
        paid.Should.HaveHttpStatus(HttpStatusCode.OK);

        var message = await Proto.Context.Messaging().AwaitAsync(
            "invoice.paid",
            candidate => candidate.MatchesShape(new { id = invoice.Id }),
            TimeSpan.FromSeconds(15));

        Assert.That(message.ContentType, Is.EqualTo("application/json"));
        message.Should.MatchShape(new { id = invoice.Id, status = InvoiceStatuses.Paid });
    }
}
