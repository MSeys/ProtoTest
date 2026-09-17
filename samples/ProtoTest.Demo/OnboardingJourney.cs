namespace ProtoTest.Demo;

using System.Net;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;

/// <summary>A new customer signs up, is constrained by the free plan, and upgrades to grow.</summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant]
[RestAuth<NorthstarAuthenticator>]
public sealed class OnboardingJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task ANewOrganizationStartsOnTheFreePlanWithAnAuditTrail()
    {
        // Act
        using var organization = await Proto.Context.Rest().GetAsync("/api/v1/organization");
        using var audit = await Proto.Context.Rest().GetAsync("/api/v1/audit");

        // Assert
        organization.ShouldHaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            planId = PlanIds.Free,
            planName = "Free",
            status = SubscriptionStatuses.Active,
            seatCount = 1,
            seatLimit = 3,
            projectCount = 0,
            projectLimit = 1,
            cancelAtPeriodEnd = false
        });
        audit.ShouldHaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            items = new[]
            {
                new { action = "organization.created", actor = JsonValue.StringContaining("@") }
            }
        });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task TheFreePlanSeatLimitRejectsTheNextInvitation()
    {
        // Arrange
        await Proto.Context.Data()
            .For<InviteMemberRequest>()
            .With(request => request.Role, MemberRoles.Developer)
            .CreateManyAsync<MembershipResponse>(2);

        // Act
        using var invitation = await Proto.Context.Rest()
            .Body(new InviteMemberRequest("overflow@example.test", MemberRoles.Viewer))
            .PostAsync("/api/v1/members");

        // Assert
        invitation.ShouldHaveHttpStatus(HttpStatusCode.PaymentRequired)
            .ShouldMatchShape(new { code = ProblemCodes.PlanLimitExceeded });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task UpgradingToGrowthLetsTheInvitationSucceed()
    {
        // Arrange
        await Proto.Context.Data()
            .For<InviteMemberRequest>()
            .With(request => request.Role, MemberRoles.Developer)
            .CreateManyAsync<MembershipResponse>(2);
        using var upgraded = await Proto.Context.Rest()
            .Body(new ChangePlanRequest(PlanIds.Growth, 10))
            .PostAsync("/api/v1/subscription");
        upgraded.ShouldHaveHttpStatus(HttpStatusCode.OK);

        // Act
        using var invitation = await Proto.Context.Rest()
            .Body(new InviteMemberRequest("overflow@example.test", MemberRoles.Viewer))
            .PostAsync("/api/v1/members");

        // Assert
        invitation.ShouldHaveHttpStatus(HttpStatusCode.Created).ShouldMatchShape(new
        {
            email = "overflow@example.test",
            role = MemberRoles.Viewer,
            status = MemberStatuses.Invited
        });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task TheFreePlanProjectLimitRejectsASecondProject()
    {
        // Arrange
        using var first = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("atlas"))
            .PostAsync("/api/v1/projects");
        first.ShouldHaveHttpStatus(HttpStatusCode.Created);

        // Act
        using var second = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("beacon"))
            .PostAsync("/api/v1/projects");

        // Assert
        second.ShouldHaveHttpStatus(HttpStatusCode.PaymentRequired).ShouldMatchShape(new
        {
            code = ProblemCodes.PlanLimitExceeded,
            details = new { limit = "1", active = "1" }
        });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task UpgradingToStarterAllowsASecondProject()
    {
        // Arrange
        await DemoSupport.CreateProjectAsync("atlas");
        using var upgraded = await Proto.Context.Rest()
            .Body(new ChangePlanRequest(PlanIds.Starter, null))
            .PostAsync("/api/v1/subscription");
        upgraded.ShouldHaveHttpStatus(HttpStatusCode.OK);

        // Act
        using var second = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("beacon"))
            .PostAsync("/api/v1/projects");

        // Assert
        second.ShouldHaveHttpStatus(HttpStatusCode.Created).ShouldMatchShape(new
        {
            name = "beacon",
            status = ProjectStatuses.Active,
            environmentCount = 0
        });
    }
}
