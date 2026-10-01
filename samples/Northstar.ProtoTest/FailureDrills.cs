namespace Northstar.ProtoTest;

using System.Net;
using System.Text.Json;
using global::NUnit.Framework;
using global::ProtoTest.Core;
using global::ProtoTest.Data;
using global::ProtoTest.Http;
using global::ProtoTest.Json;
using global::ProtoTest.NUnit;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>
/// The opt-in failures the Learn track reads, each paired with the green counterpart that does the
/// same journey the right way: time, state, environment and visibility. The last test passes but
/// carries a warning, so the run records a partial outcome and a finding. Set
/// <c>ProtoTest:Sample:Drills=true</c> to let the drills fail and record their traces; an ordinary run
/// ignores them and runs only the fixes.
/// </summary>
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
public sealed class FailureDrills
{
    /// <summary>Time, wrong: real time does not move the virtual clock, so the due window stays open.</summary>
    [ProtoTest]
    [SignedInAs]
    public async Task ARealWaitDoesNotCloseTheDueWindow()
    {
        RequireDrills();
        var invoice = await Proto.Context.Data().IssueInvoiceAsync();

        await Task.Delay(TimeSpan.FromSeconds(1));

        using var organization = await Proto.Context.Rest().GetAsync("/api/v1/organization");
        organization
            .Should.HaveHttpStatus(HttpStatusCode.OK)
            .Should.MatchShape(new { status = SubscriptionStatuses.PastDue });
    }

    /// <summary>Time, fixed: moving the test clock closes the due window and paying restores access.</summary>
    [ProtoTest]
    [SignedInAs]
    public async Task TheTestClockClosesTheDueWindow()
    {
        var invoice = await Proto.Context.Data().IssueInvoiceAsync();

        Proto.Context.Clock.Advance(TimeSpan.FromDays(8));
        using var organization = await Proto.Context.Rest().GetAsync("/api/v1/organization");
        organization
            .Should.HaveHttpStatus(HttpStatusCode.OK)
            .Should.MatchShape(new { status = SubscriptionStatuses.PastDue });

        using var paid = await Proto.Context.Rest()
            .Body(new PayInvoiceRequest(PaymentMethods.Visa))
            .PostAsync("/api/v1/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });
        paid
            .Should.HaveHttpStatus(HttpStatusCode.OK)
            .Should.MatchShape(new { status = InvoiceStatuses.Paid });
    }

    /// <summary>State, wrong: the id belongs to a record no test created.</summary>
    [ProtoTest]
    [SignedInAs]
    public async Task AnUnknownProjectIdIsTreatedAsMine()
    {
        RequireDrills();
        using var response = await Proto.Context.Rest().GetAsync("/api/v1/projects/prj_1");

        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }

    /// <summary>State, fixed: the test's own tenant is the only state it can see.</summary>
    [ProtoTest]
    [SignedInAs]
    public async Task EachTenantSeesOnlyItsOwnProjects()
    {
        var name = $"own-{Proto.Context.TestId}";
        var project = await Proto.Context.Data().CreateProjectAsync(name);

        using var response = await Proto.Context.Rest().GetAsync("/api/v1/projects");
        var page = response
            .Should.HaveHttpStatus(HttpStatusCode.OK)
            .ReadRequired<CursorPage<ProjectResponse>>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.TotalCount, Is.EqualTo(1));
            Assert.That(page.Items.Single().Id, Is.EqualTo(project.Id));
            Assert.That(page.Items.Single().Name, Is.EqualTo(name));
        }
    }

    /// <summary>Environment, wrong: the address was hardcoded for one developer machine.</summary>
    [ProtoTest]
    [SignedInAs]
    public async Task TheAddressWasHardcodedForOneMachine()
    {
        RequireDrills();
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5099") };

        try
        {
            using var response = await client.GetAsync("/api/v1/organization");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
        catch (HttpRequestException exception)
        {
            // The trace is committed and read on every machine, so record the stable error kind
            // instead of the OS text, which would change with the recorder's language.
            throw new HttpRequestException(
                $"{exception.HttpRequestError} reaching http://127.0.0.1:5099: connection refused.",
                null,
                exception.StatusCode);
        }
    }

    /// <summary>Environment, fixed: the address comes from the run's composition.</summary>
    [ProtoTest]
    [SignedInAs]
    public async Task TheAddressComesFromTheComposition()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/v1/organization");

        response
            .Should.HaveHttpStatus(HttpStatusCode.OK)
            .Should.MatchShape(new { id = JsonValue.NotNull(), status = SubscriptionStatuses.Active });
    }

    /// <summary>Visibility, wrong: a bare status hides the code and the message the application sent.</summary>
    [ProtoTest]
    [SignedInAs]
    public async Task ABareStatusHidesWhatTheApplicationSaid()
    {
        RequireDrills();
        using var response = await Proto.Context.Rest()
            .Body(new CreateProjectRequest(""))
            .PostAsync("/api/v1/projects");

        response.Should.HaveHttpStatus(HttpStatusCode.Created);
    }

    /// <summary>Visibility, fixed: the problem body names the code and what the application refused.</summary>
    [ProtoTest]
    [SignedInAs]
    public async Task TheProblemBodyNamesTheCodeAndDetail()
    {
        using var response = await Proto.Context.Rest()
            .Body(new CreateProjectRequest(""))
            .PostAsync("/api/v1/projects");

        response
            .Should.HaveHttpStatus(HttpStatusCode.BadRequest)
            .Should.MatchShape(new
            {
                code = ProblemCodes.ValidationFailed,
                message = JsonValue.StringContaining("name")
            });
    }

    /// <summary>Evidence, kept: the journey passes but names the fields it left unread, so the run records a partial outcome and a finding.</summary>
    [ProtoTest]
    [SignedInAs]
    public async Task APassingJourneyCanStillCarryAWarning()
    {
        RequireDrills();
        var name = $"warn-{Proto.Context.TestId}";
        using var created = await Proto.Context.Rest()
            .Body(new CreateProjectRequest(name))
            .PostAsync("/api/v1/projects");

        created
            .Should.HaveHttpStatus(HttpStatusCode.Created)
            .Should.MatchShape(new { name, status = ProjectStatuses.Active });

        // The shape names two fields; the response carries six. The rest went unread, so the passing
        // check warns instead of passing cleanly: the run keeps the warning as evidence.
        using var body = created.ReadRequired<JsonDocument>();
        var unread = body.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Where(field => field != "name" && field != "status")
            .OrderBy(field => field, StringComparer.Ordinal)
            .ToList();
        var message = $"The create response carried {unread.Count} fields no assertion mentioned: {string.Join(", ", unread)}.";
        Proto.Context.AddFinding(message, category: "Coverage");
        Assert.Warn(message);
    }

    internal static void RequireDrills()
    {
        var drills = Proto.Host.Configuration["ProtoTest:Sample:Drills"];
        if (!string.Equals(drills, "true", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(drills, "1", StringComparison.Ordinal))
        {
            Assert.Ignore("Set ProtoTest:Sample:Drills=true to record the deliberate failure traces.");
        }
    }
}
