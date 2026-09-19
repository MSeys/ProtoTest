namespace ProtoTest.Demo;

using ProtoTest.Core;
using ProtoTest.GraphQL;
using ProtoTest.Http;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;
using System.Net;

/// <summary>REST, GraphQL and webhooks describing the same platform.</summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant(PlanIds.Growth)]
[Auth<NorthstarAuthenticator>]
public sealed class PlatformJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task RestWritesAreVisibleThroughGraphQL()
    {
        // Arrange
        await DemoSupport.CreateProjectAsync("atlas");

        // Act
        using var projects = await Proto.Context.GraphQL()
            .Query("projects", new { first = 10 })
            .ExpectAsync(new
            {
                totalCount = 1,
                nodes = new[] { new { name = "atlas", status = ProjectStatuses.Active } }
            });

        // Assert
        projects.ShouldHaveNoErrors();
    }

    [ProtoTest]
    [SignedInAs]
    public async Task DeploymentStatusChangesStreamOverTheSubscription()
    {
        // Arrange
        var project = await DemoSupport.CreateProjectAsync("livewire");
        var preview = await DemoSupport.CreateEnvironmentAsync(project.Id, "preview", EnvironmentKinds.Preview);
        var expected = new { id = JsonValue.NotNull(), status = DeploymentStatuses.Succeeded };
        await using var subscription = await Proto.Context.GraphQL()
            .Subscription("deploymentStatusChanged")
            .Select(expected)
            .SubscribeAsync();

        // graphql-transport-ws acknowledges the connection, not each subscribe message, so a deployment
        // published before the server registers the subscription would be lost. Start the read first,
        // then keep publishing while it is pending: the read completes on the first event that reaches
        // the registered subscription, so the readiness race cannot drop it. The attempts are capped
        // with a growing pause so a broken subscription fails the test instead of flooding the API.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pending = subscription.NextAsync(timeout.Token);
        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // Act
            using var deployment = await Proto.Context.Rest()
                .Body(new CreateDeploymentRequest("1.0.0", "abc1234"))
                .PostAsync("/api/v1/environments/{environmentId}/deployments", new { environmentId = preview.Id });
            deployment.Should.HaveHttpStatus(HttpStatusCode.Created);

            var backoff = Task.Delay(TimeSpan.FromMilliseconds(Math.Min(50 * attempt, 500)));
            if (ReferenceEquals(await Task.WhenAny(pending, backoff), pending))
            {
                break;
            }
        }

        // Assert
        using var notification = await pending
            ?? throw new GraphQLAssertionException("Expected a deployment status event, but the stream completed.");
        notification.ShouldMatchShape(expected);
        notification.ShouldHaveNoErrors();
    }

    [ProtoTest]
    [SignedInAs]
    public async Task GraphQLConnectionsPageFilterAndCountDeployments()
    {
        // Arrange
        var project = await DemoSupport.CreateProjectAsync("paged");
        var preview = await DemoSupport.CreateEnvironmentAsync(project.Id, "preview", EnvironmentKinds.Preview);
        await DemoSupport.DeployAsync(preview.Id, "1.0.0", "aaa1111");
        await DemoSupport.DeployAsync(preview.Id, "1.1.0", "bbb2222");
        await DemoSupport.DeployAsync(preview.Id, "1.2.0", "ccc3333");

        // Act
        using var page = await Proto.Context.GraphQL()
            .Query("deployments", new { first = 2, status = DeploymentStatuses.Succeeded })
            .ExpectAsync(new
            {
                totalCount = 3,
                pageInfo = new { hasNextPage = true, hasPreviousPage = false },
                nodes = new[]
                {
                    new { status = DeploymentStatuses.Succeeded },
                    new { status = DeploymentStatuses.Succeeded }
                }
            });

        // Assert
        page.ShouldHaveNoErrors();
    }

    [ProtoTest]
    public async Task AnonymousGraphQLRequestsAreRejectedWithAnErrorCode()
    {
        // Act
        using var response = await Proto.Context.GraphQL()
            .WithoutAuth()
            .Query("organization")
            .Select(new { id = Gql.Field })
            .ExecuteAsync();

        // Assert
        response.ShouldHaveErrors().ShouldHaveError(ProblemCodes.Unauthorized);
    }

    [ProtoTest]
    [SignedInAs]
    public async Task WebhookDeliveriesAreSignedAndRetriedUntilTheySucceed()
    {
        // Arrange
        var sink = await DemoSupport.CreateSinkAsync(2);
        using var webhook = await Proto.Context.Rest()
            .Body(new CreateWebhookRequest(sink.Url.ToString(), [WebhookEventTypes.ProjectCreated]))
            .PostAsync("/api/v1/webhooks");
        webhook.Should.HaveHttpStatus(HttpStatusCode.Created);
        var endpoint = webhook.ReadAsJson<WebhookEndpointResponse>()!;

        // Act
        using var project = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("hooked"))
            .PostAsync("/api/v1/projects");

        // Assert
        project.Should.HaveHttpStatus(HttpStatusCode.Created);
        var delivery = await DemoSupport.WaitForDeliveredAsync(WebhookEventTypes.ProjectCreated);
        var receipt = (await DemoSupport.ReceiptsAsync(sink.Id)).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(delivery.Attempts, Is.GreaterThanOrEqualTo(3));
            Assert.That(receipt.EventType, Is.EqualTo(WebhookEventTypes.ProjectCreated));
            Assert.That(receipt.Signature, Is.EqualTo(DemoSupport.Sign(endpoint.Secret, receipt.Body)));
        }
    }
}
