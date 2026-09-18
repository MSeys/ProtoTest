namespace ProtoTest.SampleApp.Northstar;

using System.Text.Json;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;

/// <summary>The Northstar product API. Every route is tenant-scoped by the bearer token.</summary>
internal static class NorthstarApi
{
    public static void MapNorthstarApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapGet("/organization", (HttpContext http) =>
            Results.Ok(Store(http).GetOrganization(Principal(http))));

        api.MapPatch("/organization", (HttpContext http, UpdateOrganizationRequest body) =>
            Results.Ok(Store(http).UpdateOrganization(Principal(http), body.Name)));

        api.MapGet("/plans", (HttpContext http) => Results.Ok(Store(http).GetPlans()));

        api.MapGet("/subscription", (HttpContext http) =>
            Results.Ok(Store(http).GetSubscription(Principal(http))));

        api.MapPost("/subscription", (HttpContext http, ChangePlanRequest body) =>
            Results.Ok(Store(http).ChangePlan(Principal(http), body.PlanId, body.Seats)));

        api.MapPost("/subscription/cancel", (HttpContext http) =>
            Results.Ok(Store(http).CancelSubscription(Principal(http))));

        api.MapPost("/subscription/resume", (HttpContext http) =>
            Results.Ok(Store(http).ResumeSubscription(Principal(http))));

        api.MapGet("/members", (HttpContext http, string? cursor, int? limit) =>
            Results.Ok(Store(http).ListMembers(Principal(http), cursor, limit ?? 25)));

        api.MapPost("/members", (HttpContext http, InviteMemberRequest body) =>
        {
            var member = Store(http).InviteMember(Principal(http), body.Email, body.Role);
            return Results.Created($"/api/v1/members/{member.Id}", member);
        });

        api.MapPatch("/members/{memberId}", (HttpContext http, string memberId, UpdateMemberRequest body) =>
            Results.Ok(Store(http).UpdateMember(Principal(http), memberId, body.Role)));

        api.MapDelete("/members/{memberId}", (HttpContext http, string memberId) =>
        {
            Store(http).RemoveMember(Principal(http), memberId);
            return Results.NoContent();
        });

        api.MapGet("/tokens", (HttpContext http, string? cursor, int? limit) =>
            Results.Ok(Store(http).ListTokens(Principal(http), cursor, limit ?? 25)));

        api.MapPost("/tokens", (HttpContext http, CreateApiTokenRequest body) =>
        {
            var token = Store(http).CreateToken(Principal(http), body.Name, body.Scopes);
            return Results.Created($"/api/v1/tokens/{token.Token.Id}", token);
        });

        api.MapDelete("/tokens/{tokenId}", (HttpContext http, string tokenId) =>
        {
            Store(http).RevokeToken(Principal(http), tokenId);
            return Results.NoContent();
        });

        api.MapGet("/projects", (HttpContext http, string? cursor, int? limit) =>
            Results.Ok(Store(http).ListProjects(Principal(http), cursor, limit ?? 25)));

        api.MapPost("/projects", (HttpContext http, CreateProjectRequest body) =>
        {
            var project = Store(http).CreateProject(Principal(http), body.Name);
            return Results.Created($"/api/v1/projects/{project.Id}", project);
        });

    api.MapGet("/projects/{projectId}", (HttpContext http, string projectId) =>
        Results.Ok(Store(http).GetProject(Principal(http), projectId)));

    api.MapGet("/reports/monthly.xlsx", (HttpContext http) =>
    {
        var projects = Store(http).ListProjects(Principal(http), cursor: null, limit: 100).Items;
        return Results.File(
            MonthlyReportWriter.Write(projects),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "monthly.xlsx");
    });

        api.MapPost("/projects/{projectId}/archive", (HttpContext http, string projectId) =>
            Results.Ok(Store(http).ArchiveProject(Principal(http), projectId)));

        api.MapGet("/projects/{projectId}/environments", (HttpContext http, string projectId, string? cursor, int? limit) =>
            Results.Ok(Store(http).ListEnvironments(Principal(http), projectId, cursor, limit ?? 25)));

        api.MapPost("/projects/{projectId}/environments", (HttpContext http, string projectId, CreateEnvironmentRequest body) =>
        {
            var environment = Store(http).CreateEnvironment(Principal(http), projectId, body.Name, body.Kind);
            return Results.Created($"/api/v1/environments/{environment.Id}", environment);
        });

        api.MapPost("/environments/{environmentId}/deployments", (HttpContext http, string environmentId, CreateDeploymentRequest body) =>
        {
            var deployment = Store(http).Deploy(Principal(http), environmentId, body.Version, body.CommitSha);
            return Results.Created($"/api/v1/deployments/{deployment.Id}", deployment);
        });

        api.MapGet("/deployments", (HttpContext http, string? environmentId, string? projectId, string? status, string? cursor, int? limit) =>
            Results.Ok(Store(http).ListDeployments(Principal(http), environmentId, projectId, status, cursor, limit ?? 25)));

        api.MapGet("/deployments/{deploymentId}", (HttpContext http, string deploymentId) =>
            Results.Ok(Store(http).GetDeployment(Principal(http), deploymentId)));

        api.MapPost("/deployments/{deploymentId}/rollback", (HttpContext http, string deploymentId) =>
            Results.Ok(Store(http).RollbackDeployment(Principal(http), deploymentId)));

        api.MapGet("/usage", (HttpContext http, string? metric, string? cursor, int? limit) =>
            Results.Ok(Store(http).ListUsage(Principal(http), metric, cursor, limit ?? 25)));

        api.MapGet("/usage/summary", (HttpContext http, string? metric) =>
            Results.Ok(Store(http).GetUsageSummary(Principal(http), metric ?? UsageMetrics.DeployMinutes)));

        api.MapPost("/usage", (HttpContext http, RecordUsageRequest body) =>
        {
            var record = Store(http).RecordUsage(Principal(http), body.Metric, body.Quantity);
            return Results.Created($"/api/v1/usage/{record.Id}", record);
        });

        api.MapGet("/invoices", (HttpContext http, string? status, string? cursor, int? limit) =>
            Results.Ok(Store(http).ListInvoices(Principal(http), status, cursor, limit ?? 25)));

        api.MapGet("/invoices/{invoiceId:long}", (HttpContext http, long invoiceId) =>
            Results.Ok(Store(http).GetInvoice(Principal(http), invoiceId)));

        api.MapPost("/invoices/{invoiceId:long}/pay", async (HttpContext http, long invoiceId, PayInvoiceRequest body) =>
        {
            var invoice = Store(http).PayInvoice(Principal(http), invoiceId, body.Method);
            await http.RequestServices.GetRequiredService<IEventPublisher>().PublishAsync(
                "invoice.paid",
                JsonSerializer.Serialize(new { id = invoice.Id, number = invoice.Number, status = invoice.Status }),
                http.RequestAborted);
            return Results.Ok(invoice);
        });

        api.MapPost("/invoices/{invoiceId:long}/void", (HttpContext http, long invoiceId) =>
            Results.Ok(Store(http).VoidInvoice(Principal(http), invoiceId)));

        api.MapGet("/webhooks", (HttpContext http, string? cursor, int? limit) =>
            Results.Ok(Store(http).ListWebhooks(Principal(http), cursor, limit ?? 25)));

        api.MapPost("/webhooks", (HttpContext http, CreateWebhookRequest body) =>
        {
            var webhook = Store(http).CreateWebhook(Principal(http), body.Url, body.Events);
            return Results.Created($"/api/v1/webhooks/{webhook.Id}", webhook);
        });

        api.MapDelete("/webhooks/{webhookId}", (HttpContext http, string webhookId) =>
        {
            Store(http).DeleteWebhook(Principal(http), webhookId);
            return Results.NoContent();
        });

        api.MapGet("/webhook-deliveries", (HttpContext http, string? status, string? cursor, int? limit) =>
            Results.Ok(Store(http).ListWebhookDeliveries(Principal(http), status, cursor, limit ?? 25)));

        api.MapPost("/webhook-deliveries/{deliveryId}/redeliver", (HttpContext http, string deliveryId) =>
            Results.Ok(Store(http).RedeliverWebhook(Principal(http), deliveryId)));

        api.MapGet("/audit", (HttpContext http, string? cursor, int? limit) =>
            Results.Ok(Store(http).ListAudit(Principal(http), cursor, limit ?? 25)));
    }

    private static NorthstarStore Store(HttpContext http) => NorthstarHttp.Store(http);

    private static NorthstarPrincipal Principal(HttpContext http) => NorthstarHttp.Principal(http);
}
