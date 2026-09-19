namespace ProtoTest.Demo;

using Northstar.ProtoTest;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.Http;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using System.Net;

/// <summary>Who may do what: the role matrix, token scopes, tenant isolation and rate limits.</summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant(PlanIds.Growth)]
[SignedInAs]
[Auth<NorthstarAuthenticator>]
public sealed class AccessJourney
{
    [ProtoTest]
    public async Task DevelopersAndBillingContactsCannotCreateProjects()
    {
        // Arrange
        var developer = await Proto.Context.Data().CreateMemberTokenAsync(MemberRoles.Developer);
        var billing = await Proto.Context.Data().CreateMemberTokenAsync(MemberRoles.Billing);

        // Act
        using var developerAttempt = await DemoSupport.As(developer)
            .Body(new CreateProjectRequest("dev-project"))
            .PostAsync("/api/v1/projects");
        using var billingAttempt = await DemoSupport.As(billing)
            .Body(new CreateProjectRequest("billing-project"))
            .PostAsync("/api/v1/projects");

        // Assert
        developerAttempt.Should.HaveHttpStatus(HttpStatusCode.Forbidden)
            .ShouldMatchShape(new { code = ProblemCodes.Forbidden });
        billingAttempt.Should.HaveHttpStatus(HttpStatusCode.Forbidden)
            .ShouldMatchShape(new { code = ProblemCodes.Forbidden });
    }

    [ProtoTest]
    public async Task AdministratorsCanCreateProjects()
    {
        // Arrange
        var owner = await Proto.Context.Data().CreateMemberTokenAsync(MemberRoles.Owner);

        // Act
        using var project = await DemoSupport.As(owner)
            .Body(new CreateProjectRequest("owner-project"))
            .PostAsync("/api/v1/projects");

        // Assert
        project.Should.HaveHttpStatus(HttpStatusCode.Created)
            .ShouldMatchShape(new { name = "owner-project", status = ProjectStatuses.Active });
    }

    [ProtoTest]
    public async Task DevelopersMayDeployToPreviewButOnlyAdministratorsToProduction()
    {
        // Arrange
        var owner = await Proto.Context.Data().CreateMemberTokenAsync(MemberRoles.Owner);
        var developer = await Proto.Context.Data().CreateMemberTokenAsync(MemberRoles.Developer);
        var project = await Proto.Context.Data().CreateProjectAsync("matrix");
        var preview = await Proto.Context.Data().CreateEnvironmentAsync(project.Id, "preview", EnvironmentKinds.Preview);
        var production = await Proto.Context.Data().CreateEnvironmentAsync(project.Id, "production", EnvironmentKinds.Production);

        // Act
        using var previewDeploy = await DemoSupport.As(developer)
            .Body(new CreateDeploymentRequest("1.0.0", "abc1234"))
            .PostAsync("/api/v1/environments/{environmentId}/deployments", new { environmentId = preview.Id });
        using var productionAttempt = await DemoSupport.As(developer)
            .Body(new CreateDeploymentRequest("1.0.0", "abc1234"))
            .PostAsync("/api/v1/environments/{environmentId}/deployments", new { environmentId = production.Id });
        using var productionDeploy = await DemoSupport.As(owner)
            .Body(new CreateDeploymentRequest("1.0.0", "abc1234"))
            .PostAsync("/api/v1/environments/{environmentId}/deployments", new { environmentId = production.Id });

        // Assert
        previewDeploy.Should.HaveHttpStatus(HttpStatusCode.Created)
            .ShouldMatchShape(new { status = DeploymentStatuses.Succeeded });
        productionAttempt.Should.HaveHttpStatus(HttpStatusCode.Forbidden)
            .ShouldMatchShape(new { code = ProblemCodes.Forbidden });
        productionDeploy.Should.HaveHttpStatus(HttpStatusCode.Created);
    }

    [ProtoTest]
    public async Task ViewersCannotReadTheAuditLog()
    {
        // Arrange
        var viewer = await Proto.Context.Data().CreateMemberTokenAsync(MemberRoles.Viewer);

        // Act
        using var audit = await DemoSupport.As(viewer).GetAsync("/api/v1/audit");

        // Assert
        audit.Should.HaveHttpStatus(HttpStatusCode.Forbidden)
            .ShouldMatchShape(new { code = ProblemCodes.Forbidden });
    }

    [ProtoTest]
    public async Task BillingContactsCanReadTheAuditLog()
    {
        // Arrange
        var billing = await Proto.Context.Data().CreateMemberTokenAsync(MemberRoles.Billing);

        // Act
        using var audit = await DemoSupport.As(billing).GetAsync("/api/v1/audit");

        // Assert
        audit.Should.HaveHttpStatus(HttpStatusCode.OK);
    }

    [ProtoTest]
    public async Task ReadOnlyApiTokensCannotDeployEvenForAnOwner()
    {
        // Arrange
        var owner = await Proto.Context.Data().CreateMemberTokenAsync(MemberRoles.Owner);
        using var createdToken = await DemoSupport.As(owner)
            .Body(new CreateApiTokenRequest("ci-readonly", [TokenScopes.Read]))
            .PostAsync("/api/v1/tokens");
        createdToken.Should.HaveHttpStatus(HttpStatusCode.Created);
        var readOnly = createdToken.ReadAsJson<ApiTokenSecretResponse>()!.Secret;
        var project = await Proto.Context.Data().CreateProjectAsync("scoped");
        var preview = await Proto.Context.Data().CreateEnvironmentAsync(project.Id, "preview", EnvironmentKinds.Preview);

        // Act
        using var deployment = await DemoSupport.As(readOnly)
            .Body(new CreateDeploymentRequest("1.0.0", "abc1234"))
            .PostAsync("/api/v1/environments/{environmentId}/deployments", new { environmentId = preview.Id });

        // Assert
        deployment.Should.HaveHttpStatus(HttpStatusCode.Forbidden)
            .ShouldMatchShape(new { code = ProblemCodes.Forbidden });
    }

    [ProtoTest]
    public async Task OneOrganizationsTokenCannotReadAnotherOrganizationsResources()
    {
        // Arrange
        var owner = await Proto.Context.Data().CreateMemberTokenAsync(MemberRoles.Owner);
        var intruder = await Proto.Context.Data()
            .For<ProvisionTenantRequest>()
            .With(request => request.Name, $"northstar-intruder-{Proto.Context.TestId}")
            .With(request => request.PlanId, PlanIds.Free)
            .CreateAsync<TenantResponse>();
        using var secret = await DemoSupport.As(intruder.OwnerToken)
            .Body(new CreateProjectRequest("secret"))
            .PostAsync("/api/v1/projects");
        secret.Should.HaveHttpStatus(HttpStatusCode.Created);
        var secretProject = secret.ReadAsJson<ProjectResponse>()!;

        // Act
        using var attempt = await DemoSupport.As(owner)
            .GetAsync("/api/v1/projects/{projectId}", new { projectId = secretProject.Id });

        // Assert
        attempt.Should.HaveHttpStatus(HttpStatusCode.NotFound)
            .ShouldMatchShape(new { code = ProblemCodes.NotFound });
    }

    [ProtoTest]
    public async Task ExceedingTheTokenRateLimitReturnsTooManyRequests()
    {
        // Arrange
        var owner = await Proto.Context.Data().CreateMemberTokenAsync(MemberRoles.Owner);

        // Act
        for (var request = 0; request < 60; request++)
        {
            using var allowed = await DemoSupport.As(owner).GetAsync("/api/v1/organization");
        }

        using var limited = await DemoSupport.As(owner).GetAsync("/api/v1/organization");

        // Assert
        limited.Should.HaveHttpStatus(HttpStatusCode.TooManyRequests)
            .ShouldMatchShape(new { code = ProblemCodes.RateLimited });
    }
}
