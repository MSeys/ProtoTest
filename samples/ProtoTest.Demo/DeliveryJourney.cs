namespace ProtoTest.Demo;

using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;
using System.Net;

/// <summary>A team ships a release through preview, promotes it, then rolls it back.</summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant(PlanIds.Growth)]
[Auth<NorthstarAuthenticator>]
public sealed class DeliveryJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task DeployingToThePreviewEnvironmentPublishesTheVersion()
    {
        // Arrange
        var project = await DemoSupport.CreateProjectAsync("orion");
        var preview = await DemoSupport.CreateEnvironmentAsync(project.Id, "preview", EnvironmentKinds.Preview);

        // Act
        using var deployment = await Proto.Context.Rest()
            .Body(new CreateDeploymentRequest("2026.09.1", "a1b2c3d"))
            .PostAsync("/api/v1/environments/{environmentId}/deployments", new { environmentId = preview.Id });

        // Assert
        deployment.Should.HaveHttpStatus(HttpStatusCode.Created).ShouldMatchShape(new
        {
            environmentId = preview.Id,
            version = "2026.09.1",
            status = DeploymentStatuses.Succeeded,
            deployMinutes = JsonValue.GreaterThan(0)
        });
        using var environments = await Proto.Context.Rest()
            .GetAsync("/api/v1/projects/{projectId}/environments", new { projectId = project.Id });
        environments.Should.HaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            items = new[]
            {
                new { name = "preview", kind = EnvironmentKinds.Preview, currentVersion = "2026.09.1" }
            }
        });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task TheSameReleaseCanBePromotedToProduction()
    {
        // Arrange
        var project = await DemoSupport.CreateProjectAsync("orion");
        var preview = await DemoSupport.CreateEnvironmentAsync(project.Id, "preview", EnvironmentKinds.Preview);
        var production = await DemoSupport.CreateEnvironmentAsync(project.Id, "production", EnvironmentKinds.Production);
        await DemoSupport.DeployAsync(preview.Id, "2026.09.1", "a1b2c3d");

        // Act
        using var promoted = await Proto.Context.Rest()
            .Body(new CreateDeploymentRequest("2026.09.1", "d4e5f6a"))
            .PostAsync("/api/v1/environments/{environmentId}/deployments", new { environmentId = production.Id });

        // Assert
        promoted.Should.HaveHttpStatus(HttpStatusCode.Created).ShouldMatchShape(new
        {
            environmentId = production.Id,
            status = DeploymentStatuses.Succeeded,
            version = "2026.09.1"
        });
        using var deployments = await Proto.Context.Rest()
            .GetAsync("/api/v1/deployments", new { projectId = project.Id });
        deployments.Should.HaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new { totalCount = 2 });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task AFailedBuildIsRecordedButNeverBecomesTheCurrentVersion()
    {
        // Arrange
        var project = await DemoSupport.CreateProjectAsync("vega");
        var preview = await DemoSupport.CreateEnvironmentAsync(project.Id, "preview", EnvironmentKinds.Preview);

        // Act
        using var failed = await Proto.Context.Rest()
            .Body(new CreateDeploymentRequest("2.0.0", "badc0ffee"))
            .PostAsync("/api/v1/environments/{environmentId}/deployments", new { environmentId = preview.Id });

        // Assert
        failed.Should.HaveHttpStatus(HttpStatusCode.Created).ShouldMatchShape(new
        {
            version = "2.0.0",
            status = DeploymentStatuses.Failed
        });
        using var environments = await Proto.Context.Rest()
            .GetAsync("/api/v1/projects/{projectId}/environments", new { projectId = project.Id });
        var current = environments.ReadAsJson<CursorPage<EnvironmentResponse>>()!.Items.Single(item => item.Id == preview.Id);
        Assert.That(current.CurrentVersion, Is.Null);
    }

    [ProtoTest]
    [SignedInAs]
    public async Task RollingBackTheCurrentReleaseRestoresThePreviousVersion()
    {
        // Arrange
        var project = await DemoSupport.CreateProjectAsync("lyra");
        var preview = await DemoSupport.CreateEnvironmentAsync(project.Id, "preview", EnvironmentKinds.Preview);
        await DemoSupport.DeployAsync(preview.Id, "1.0.0", "aaaa111");
        var current = await DemoSupport.DeployAsync(preview.Id, "1.1.0", "bbbb222");

        // Act
        using var rolledBack = await Proto.Context.Rest()
            .PostAsync("/api/v1/deployments/{deploymentId}/rollback", new { deploymentId = current.Id });

        // Assert
        rolledBack.Should.HaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            id = current.Id,
            status = DeploymentStatuses.RolledBack
        });
        using var environments = await Proto.Context.Rest()
            .GetAsync("/api/v1/projects/{projectId}/environments", new { projectId = project.Id });
        var restored = environments.ReadAsJson<CursorPage<EnvironmentResponse>>()!.Items.Single(item => item.Id == preview.Id);
        Assert.That(restored.CurrentVersion, Is.EqualTo("1.0.0"));
    }
}
