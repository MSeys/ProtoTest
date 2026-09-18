namespace ProtoTest.Demo;

using System.Net;
using global::NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.Http;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;
using ProtoTest.SampleApp.Testing;
using ProtoTest.Sheets;

/// <summary>
/// The generated monthly report: the application writes a real OpenXML workbook, the test downloads it
/// and asserts it through the record model - no spreadsheet library involved on either side.
/// </summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant(PlanIds.Growth)]
[Auth<NorthstarAuthenticator>]
public sealed class SheetsJourney
{
    [Sheet("Summary", HeaderRows = [1])]
    public sealed record ProjectReportRow(
        [property: Column("Name", Unique = true)] string Name,
        [property: Column("Status", Pattern = "^[a-z]+$")] string Status,
        [property: Column("Environments", Min = 0)] int Environments);

    [ProtoTest]
    [SignedInAs]
    public async Task TheMonthlyReport_ShouldMatchItsModel()
    {
        // Arrange: a project the report must contain.
        var project = await Proto.Context.Data()
            .For<CreateProjectRequest>()
            .With(request => request.Name, "report-atlas")
            .CreateAsync<ProjectResponse>();

        // Act: download the generated workbook.
        using var response = await Proto.Context.Rest().GetAsync("/api/v1/reports/monthly.xlsx");
        response.ShouldHaveHttpStatus(HttpStatusCode.OK);
        using var stream = new MemoryStream(response.ContentBytes.ToArray());
        var report = Proto.Context.Sheets().Open(stream, "monthly.xlsx").Model<ProjectReportRow>();

        // Assert: the model checks the layout, and the exact values are ordinary assertions.
        report.Verify();
        report.Column(row => row.Environments).ShouldAll(count => count >= 0);
        var row = report.Row(candidate => candidate.Name == "report-atlas");
        Assert.Multiple(() =>
        {
            Assert.That(row.Status, Is.EqualTo(project.Status));
            Assert.That(row.Environments, Is.EqualTo(0));
        });
    }
}
