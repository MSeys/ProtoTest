namespace Northstar.ProtoTest;

using System.Net;
using global::NUnit.Framework;
using global::ProtoTest.Core;
using global::ProtoTest.Data;
using global::ProtoTest.Http;
using global::ProtoTest.NUnit;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;
using global::ProtoTest.Sheets;

/// <summary>
/// The generated monthly report: the application writes a real OpenXML workbook, the test downloads it
/// and asserts it through the record model, with no spreadsheet library involved on either side.
/// </summary>
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
public sealed class SheetsJourney
{
    [Sheet("Summary", HeaderRows = [1])]
    public sealed record ProjectReportRow(
        [property: Column("Name", Unique = true)] string Name,
        [property: Column("Status", Pattern = "^[a-z]+$")] string Status,
        [property: Column("Environments", Min = 0)] int Environments);

    [ProtoTest]
    [SignedInAs]
    public async Task TheMonthlyReportMatchesItsModel()
    {
        // Arrange: a project the report must contain.
        var project = await Proto.Context.Data()
            .For<CreateProjectRequest>()
            .With(request => request.Name, "report-atlas")
            .CreateAsync<ProjectResponse>();

        // Act: download the generated workbook; the response is content the sheet reader understands.
        using var response = await Proto.Context.Rest().GetAsync("/api/v1/reports/monthly.xlsx");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        var report = Proto.Context.Sheets().Open(response).Model<ProjectReportRow>();

        // Assert: the model checks the layout, and the exact values are ordinary assertions.
        report.Should.MatchModel();
        report.Column(row => row.Environments).Should.All(count => count >= 0);
        var row = report.Row(candidate => candidate.Name == "report-atlas");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Status, Is.EqualTo(project.Status));
            Assert.That(row.Environments, Is.EqualTo(0));
        }
    }
}
