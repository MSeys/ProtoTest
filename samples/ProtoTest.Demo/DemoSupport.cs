namespace ProtoTest.Demo;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;

/// <summary>Small helpers shared by the journeys so each test reads as the story it tells.</summary>
internal static class DemoSupport
{
    public static RestRequestBuilder As(string token)
        => Proto.Context.Rest().WithoutAuth().Header("Authorization", $"Bearer {token}");

    public static async Task<ProjectResponse> CreateProjectAsync(string name)
    {
        using var response = await Proto.Context.Rest()
            .Body(new CreateProjectRequest(name))
            .PostAsync("/api/v1/projects");
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        return response.ReadAsJson<ProjectResponse>()!;
    }

    public static async Task<EnvironmentResponse> CreateEnvironmentAsync(string projectId, string name, string kind)
    {
        using var response = await Proto.Context.Rest()
            .Body(new CreateEnvironmentRequest(name, kind))
            .PostAsync("/api/v1/projects/{projectId}/environments", new { projectId });
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        return response.ReadAsJson<EnvironmentResponse>()!;
    }

    public static async Task<DeploymentResponse> DeployAsync(string environmentId, string version, string commitSha)
    {
        using var response = await Proto.Context.Rest()
            .Body(new CreateDeploymentRequest(version, commitSha))
            .PostAsync("/api/v1/environments/{environmentId}/deployments", new { environmentId });
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        return response.ReadAsJson<DeploymentResponse>()!;
    }

    public static async Task<ClockResponse> AdvanceClockAsync(int days)
    {
        var organization = Proto.Context.Resolve<NorthstarOrganizationContext>();
        using var response = await Proto.Context.Rest()
            .WithoutAuth()
            .Body(new AdvanceClockRequest(Days: days))
            .PostAsync("/test-support/tenants/{tenant}/clock/advance", new { tenant = organization.Tenant });
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        return response.ReadAsJson<ClockResponse>()!;
    }

    public static async Task<InvoiceResponse> IssueInvoiceAsync()
    {
        using var usage = await Proto.Context.Rest()
            .Body(new RecordUsageRequest(UsageMetrics.DeployMinutes, 1))
            .PostAsync("/api/v1/usage");
        usage.Should.HaveHttpStatus(HttpStatusCode.Created);
        await AdvanceClockAsync(31);

        using var invoices = await Proto.Context.Rest()
            .GetAsync("/api/v1/invoices", new { status = InvoiceStatuses.Open });
        invoices.Should.HaveHttpStatus(HttpStatusCode.OK);
        return invoices.ReadAsJson<CursorPage<InvoiceResponse>>()!.Items.Single();
    }

    public static async Task<WebhookSinkResponse> CreateSinkAsync(int failuresBeforeSuccess)
    {
        using var response = await Proto.Context.Rest()
            .WithoutAuth()
            .Body(new ConfigureWebhookSinkRequest(null, failuresBeforeSuccess))
            .PostAsync("/test-support/webhook-sinks");
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        return response.ReadAsJson<WebhookSinkResponse>()!;
    }

    public static async Task<IReadOnlyList<WebhookReceiptResponse>> ReceiptsAsync(string sinkId)
    {
        using var response = await Proto.Context.Rest()
            .WithoutAuth()
            .GetAsync("/test-support/webhook-sinks/{sinkId}/receipts", new { sinkId });
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        return response.ReadAsJson<IReadOnlyList<WebhookReceiptResponse>>()!;
    }

    public static async Task<WebhookDeliveryResponse> WaitForDeliveredAsync(string eventType)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await Proto.Context.Rest()
                .GetAsync("/api/v1/webhook-deliveries", new { status = WebhookDeliveryStatuses.Delivered });
            var page = response.ReadAsJson<CursorPage<WebhookDeliveryResponse>>()!;
            var match = page.Items.FirstOrDefault(delivery => delivery.EventType == eventType);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"No delivered '{eventType}' webhook arrived within the timeout.");
    }

    public static async Task<string> TokenForAsync(string role)
    {
        var organization = Proto.Context.Resolve<NorthstarOrganizationContext>();
        if (role == MemberRoles.Owner)
        {
            return organization.OwnerToken;
        }

        using var response = await Proto.Context.Rest()
            .WithoutAuth()
            .Body(new InviteMemberRequest($"{role}.{Proto.Context.TestId}@example.test", role))
            .PostAsync("/test-support/tenants/{tenant}/members", new { tenant = organization.Tenant });
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        return response.ReadAsJson<TestMemberResponse>()!.Token;
    }

    public static string Sign(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return $"sha256={Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()}";
    }
}
