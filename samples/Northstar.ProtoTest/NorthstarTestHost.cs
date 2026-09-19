namespace Northstar.ProtoTest;

using Microsoft.Extensions.DependencyInjection;
using global::ProtoTest.Core;
using global::ProtoTest.Data;
using global::ProtoTest.GraphQL;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>Options for the Northstar test-support surface: the scenario hook and its clients.</summary>
public sealed class NorthstarTestSupportOptions
{
    /// <summary>
    /// Whether GraphQL subscriptions ride the in-process test server's WebSocket. Leave false against a
    /// published environment, where the client's own transport must be used.
    /// </summary>
    public bool UseInProcessGraphQLWebSockets { get; set; }
}

/// <summary>Options for Northstar data provisioning.</summary>
public sealed class NorthstarDataOptions
{
    /// <summary>
    /// Whether fixtures are created through the Northstar domain over the store the suite shares with
    /// the application. True only when a reachable store is composed; otherwise the same fixtures are
    /// provisioned through the public API (and the test-support surface where nothing else can do it),
    /// so a published environment without a store still runs.
    /// </summary>
    public bool UseDomainProvisioners { get; set; }
}

/// <summary>
/// Registers everything Northstar's journeys need beyond the protocol integrations: the scenario hook,
/// the tenant/member attributes' backing contexts, and the data provisioners.
/// </summary>
public static class NorthstarTestHost
{
    /// <summary>
    /// Adds the Northstar scenario layer: the correlation hook, its custom-client probe, and - when
    /// asked - the GraphQL WebSocket factory that connects through the in-process test server.
    /// </summary>
    public static IProtoHostBuilder AddNorthstarTestSupport(
        this IProtoHostBuilder builder,
        Action<NorthstarTestSupportOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new NorthstarTestSupportOptions();
        configure?.Invoke(options);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IProtoClientInitializer, ScenarioProbeInitializer>();
            if (options.UseInProcessGraphQLWebSockets)
            {
                services.AddSingleton<IGraphQLWebSocketFactory, NorthstarGraphQLWebSocketFactory>();
            }
        });
        return builder.AddTestHook<NorthstarScenarioHook>();
    }

    /// <summary>
    /// Adds Northstar data provisioning: the member default, the deliberate test-support affordances
    /// (the tenant clock and the in-process webhook sink), one portable API provisioner, and either the
    /// domain provisioners or their API fallbacks for every other fixture.
    /// </summary>
    public static IProtoHostBuilder AddNorthstarData(
        this IProtoHostBuilder builder,
        Action<NorthstarDataOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new NorthstarDataOptions();
        configure?.Invoke(options);

        builder.AddData(data => data.AddDefaults<NorthstarDataDefaults>());

        // Portable API provisioning: members go through POST /api/v1/members in every environment.
        builder.AddDataProvisioner<InviteMemberRequest, MembershipResponse, NorthstarMemberProvisioner>();

        // Only the running application can move its own clock or steer its in-process webhook sink.
        builder.AddDataProvisioner<AdvanceClockRequest, ClockResponse, NorthstarClockProvisioner>();
        builder.AddDataProvisioner<ConfigureWebhookSinkRequest, WebhookSinkResponse, NorthstarWebhookSinkProvisioner>();

        if (options.UseDomainProvisioners)
        {
            builder.AddDataProvisioner<ProvisionTenantRequest, TenantResponse, NorthstarTenantProvisioner>();
            builder.AddDataProvisioner<CreateProjectRequest, ProjectResponse, NorthstarProjectProvisioner>();
            builder.AddDataProvisioner<ProvisionEnvironmentRequest, EnvironmentResponse, NorthstarEnvironmentProvisioner>();
            builder.AddDataProvisioner<ProvisionDeploymentRequest, DeploymentResponse, NorthstarDeploymentProvisioner>();
            builder.AddDataProvisioner<IssueInvoiceRequest, InvoiceResponse, NorthstarInvoiceProvisioner>();
            builder.AddDataProvisioner<InviteMemberRequest, TestMemberResponse, NorthstarTestMemberProvisioner>();
        }
        else
        {
            builder.AddDataProvisioner<ProvisionTenantRequest, TenantResponse, NorthstarTestSupportTenantProvisioner>();
            builder.AddDataProvisioner<CreateProjectRequest, ProjectResponse, NorthstarApiProjectProvisioner>();
            builder.AddDataProvisioner<ProvisionEnvironmentRequest, EnvironmentResponse, NorthstarApiEnvironmentProvisioner>();
            builder.AddDataProvisioner<ProvisionDeploymentRequest, DeploymentResponse, NorthstarApiDeploymentProvisioner>();
            builder.AddDataProvisioner<IssueInvoiceRequest, InvoiceResponse, NorthstarApiInvoiceProvisioner>();
            builder.AddDataProvisioner<InviteMemberRequest, TestMemberResponse, NorthstarTestSupportMemberProvisioner>();
        }

        return builder;
    }
}
