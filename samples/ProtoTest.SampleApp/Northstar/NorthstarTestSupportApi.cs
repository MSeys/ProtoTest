namespace ProtoTest.SampleApp.Northstar;

using ProtoTest.SampleApp.Contracts;

/// <summary>
/// Scenario provisioning and control, deliberately kept out of the product surface. Every route lives
/// under <c>/test-support</c> so it is obvious what would not ship to production.
/// </summary>
internal static class NorthstarTestSupportApi
{
    public static void MapNorthstarTestSupport(this WebApplication app)
    {
        app.MapPost("/test-support/tenants", (HttpContext http, ProvisionTenantRequest body) =>
        {
            var baseUrl = new Uri($"{http.Request.Scheme}://{http.Request.Host}");
            var tenant = NorthstarHttp.Store(http).ProvisionTenant(body.Name, body.PlanId, baseUrl);
            return Results.Created($"/test-support/tenants/{tenant.Tenant}", tenant);
        });

        app.MapDelete("/test-support/tenants/{slug}", (HttpContext http, string slug) =>
        {
            NorthstarHttp.Store(http).DeleteTenant(slug);
            return Results.NoContent();
        });

        app.MapPost("/test-support/tenants/{slug}/members", (HttpContext http, string slug, InviteMemberRequest body) =>
        {
            var (membership, token) = NorthstarHttp.Store(http).CreateMemberForRole(slug, body.Email, body.Role);
            return Results.Created($"/test-support/tenants/{slug}/members", new TestMemberResponse(membership, token));
        });

        app.MapPost("/test-support/tenants/{slug}/clock/advance", (HttpContext http, string slug, AdvanceClockRequest body) =>
        {
            var delta = TimeSpan.FromDays(body.Days) + TimeSpan.FromHours(body.Hours) + TimeSpan.FromMinutes(body.Minutes);
            return Results.Ok(NorthstarHttp.Store(http).AdvanceClock(slug, delta));
        });

        app.MapPost("/test-support/webhook-sinks", (HttpContext http, ConfigureWebhookSinkRequest body) =>
        {
            var baseUrl = new Uri($"{http.Request.Scheme}://{http.Request.Host}");
            var (id, url) = http.RequestServices.GetRequiredService<WebhookSinkRegistry>()
                .Create(baseUrl, body.FailuresBeforeSuccess);
            return Results.Created($"/test-support/webhook-sinks/{id}", new WebhookSinkResponse(id, url));
        });

        app.MapGet("/test-support/webhook-sinks/{sinkId}/receipts", (HttpContext http, string sinkId) =>
            Results.Ok(http.RequestServices.GetRequiredService<WebhookSinkRegistry>().Receipts(sinkId)));

        app.MapPost("/test-support/webhook-sinks/{sinkId}", async (HttpContext http, string sinkId) =>
        {
            using var reader = new StreamReader(http.Request.Body);
            var body = await reader.ReadToEndAsync();
            var signature = http.Request.Headers["X-Northstar-Signature"].ToString();
            var eventType = http.Request.Headers["X-Northstar-Event"].ToString();
            var (success, error) = http.RequestServices.GetRequiredService<WebhookSinkRegistry>()
                .Receive(sinkId, eventType, signature, body);
            return success
                ? Results.Ok()
                : Results.Json(new ProblemResponse(ProblemCodes.Conflict, error ?? "sink_failed"), statusCode: StatusCodes.Status503ServiceUnavailable);
        });
    }
}
