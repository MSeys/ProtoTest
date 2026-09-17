import type {ComparisonFile} from '@site/src/components/Comparison';

/**
 * The same scenario — a billing administrator creates an order and reconciles
 * open invoices against an in-process ASP.NET Core app — written twice, with
 * every supporting file each version actually needs.
 *
 * `infrastructureLines` marks lines that stand the test up rather than
 * describe the scenario. Blank lines are excluded from the counts.
 */

const withoutTest = `namespace Billing.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NUnit.Framework;

[TestFixture]
public sealed class BillingTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private TestEnvironment _environment = null!;
    private TestUser _user = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() =>
        _factory = new WebApplicationFactory<Program>();

    [SetUp]
    public async Task SetUp()
    {
        _environment = await Environments.CreateAsync();
        _user = await Users.CreateAsync(_environment, Roles.BillingAdministrator);

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new("Bearer", _user.AccessToken);
        _client.DefaultRequestHeaders.Add("X-Tenant", _environment.Tenant);
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await Environments.DeleteAsync(_environment);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _factory.Dispose();

    [Test]
    public async Task BillingAdministratorCanReconcileOpenInvoices()
    {
        using var created = await _client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest("observability-seat", 12, 19.95m));

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var order = await created.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(order!.Product, Is.EqualTo("observability-seat"));
            Assert.That(order.Quantity, Is.EqualTo(12));
            Assert.That(order.Total, Is.GreaterThan(200m));
            Assert.That(order.Status, Is.EqualTo("pending"));
        });

        using var invoices = await _client.GetAsync("/api/billing/invoices?state=open");

        Assert.That(invoices.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var page = await invoices.Content.ReadFromJsonAsync<InvoicePage>();

        Assert.Multiple(() =>
        {
            Assert.That(page!.State, Is.EqualTo("open"));
            Assert.That(page.Invoices, Is.Not.Empty);
            Assert.That(page.Invoices[0].Id, Is.GreaterThan(0));
            Assert.That(page.Invoices[0].State, Is.EqualTo("open"));
            Assert.That(page.Invoices[0].Total, Is.GreaterThan(0m));
        });
    }
}`;

const withoutSupport = `namespace Billing.Tests;

using System.Net.Http.Json;

public static class Environments
{
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("http://localhost:5080")
    };

    public static async Task<TestEnvironment> CreateAsync()
    {
        var name = $"test-{Guid.NewGuid():N}";
        using var response = await Client.PostAsJsonAsync(
            "/test-support/environments",
            new CreateEnvironmentRequest(name));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TestEnvironment>())!;
    }

    public static async Task DeleteAsync(TestEnvironment environment)
    {
        using var response = await Client.DeleteAsync(
            $"/test-support/environments/{environment.Tenant}");
        response.EnsureSuccessStatusCode();
    }
}

public static class Users
{
    public static async Task<TestUser> CreateAsync(TestEnvironment environment, string role)
    {
        using var client = new HttpClient { BaseAddress = environment.ApiUrl };
        using var response = await client.PostAsJsonAsync(
            $"/test-support/environments/{environment.Tenant}/users",
            new CreateUserRequest($"{role}.{Guid.NewGuid():N}@example.test", role));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TestUser>())!;
    }
}`;

const withoutDtos = `namespace Billing.Tests;

public sealed record TestEnvironment(string Tenant, string Name, Uri ApiUrl);

public sealed record TestUser(
    string Id, string Tenant, string Email, string Role, string AccessToken);

public sealed record OrderResponse(
    int Id, string Product, int Quantity, decimal Total, string Status);

public sealed record InvoicePage(string State, IReadOnlyList<Invoice> Invoices);

public sealed record Invoice(int Id, string State, decimal Total);`;

const withTest = `namespace Billing.Tests;

using System.Net;
using ProtoTest.Core;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[RestClient(SampleAppTargets.Api)]
[SampleEnvironment]
[Auth<SampleUserAuthenticator>]
public sealed class BillingTests
{
    [ProtoTest]
    [SampleUser(SampleRoles.BillingAdministrator)]
    public async Task BillingAdministratorCanReconcileOpenInvoices()
    {
        using var created = await Proto.Context.Rest()
            .Body(new CreateOrderRequest("observability-seat", 12, 19.95m))
            .PostAsync("/api/orders");

        created.ShouldHaveStatus(HttpStatusCode.Created).ShouldMatchShape(new
        {
            product = "observability-seat",
            quantity = 12,
            total = JsonValue.GreaterThan(200m),
            status = "pending"
        });

        using var invoices = await Proto.Context.Rest()
            .GetAsync("/api/billing/invoices", new { state = "open" });

        invoices.ShouldHaveStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            state = "open",
            invoices = new[]
            {
                new { id = JsonValue.GreaterThan(0), state = "open" }
            }
        });
    }
}`;

const withSetup = `namespace Billing.Tests;

using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.OpenApi;
using ProtoTest.Reporting;
using ProtoTest.Rest;

[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) =>
        builder
            .AddRest(rest => rest
                .AddClient(SampleAppTargets.Api)
                .WithCollector<RestCoverageCollector>()
                .WithCollector<OpenApiCoverageCollector>())
            .AddAspNetCoreServer<Program>(SampleAppTargets.Api)
            .ConfigureTracing(trace =>
                trace.OutputPath = "TestResults/billing.prototrace")
            .AddSink<HtmlReportSink>(sink =>
                sink.OutputPath = "TestResults/report.html");
}`;

const withScenario = `namespace Billing.Tests;

using System.Net;
using System.Net.Http.Headers;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Rest;

public sealed class SampleEnvironmentAttribute : ProtoAttribute
{
    public SampleEnvironmentAttribute() => Order = -200;

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        using var response = await context.Rest(SampleAppTargets.Api)
            .WithoutAuth()
            .Body(new CreateEnvironmentRequest($"test-{context.TestId}"))
            .PostAsync("/test-support/environments");

        var environment = response
            .ShouldHaveStatus(HttpStatusCode.Created)
            .ReadAsJson<EnvironmentResponse>()!;

        context.SetContext(new SampleEnvironmentContext(
            environment.Tenant, environment.Name, environment.ApiBaseUrl));
    }

    public override async Task AfterTestAsync(ProtoExecutionContext context)
    {
        var environment = context.TryContext<SampleEnvironmentContext>();
        if (environment is null) return;

        using var response = await context.Rest(SampleAppTargets.Api)
            .WithoutAuth()
            .DeleteAsync("/test-support/environments/{tenant}",
                new { environment.Tenant });
        response.ShouldHaveStatus(HttpStatusCode.NoContent);
    }
}

public sealed class SampleUserAttribute(string role) : ProtoAttribute
{
    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var environment = context.Context<SampleEnvironmentContext>();
        using var response = await context.Rest(SampleAppTargets.Api)
            .WithoutAuth()
            .Body(new CreateUserRequest($"{role}.{context.TestId}@example.test", role))
            .PostAsync("/test-support/environments/{tenant}/users",
                new { environment.Tenant });

        var user = response
            .ShouldHaveStatus(HttpStatusCode.Created)
            .ReadAsJson<UserResponse>()!;

        context.SetContext(new SampleUserContext(
            user.Id, user.Tenant, user.Email, user.Role, user.AccessToken));
    }
}

public sealed class SampleUserAuthenticator : IProtoHttpAuthenticator
{
    public ValueTask AuthenticateAsync(
        ProtoHttpAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        var environment = context.Test.Context<SampleEnvironmentContext>();
        var user = context.Test.Context<SampleUserContext>();

        context.Request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", user.AccessToken);
        context.Request.Headers.Add("X-Tenant", environment.Tenant);
        return ValueTask.CompletedTask;
    }
}`;

export const withoutProtoTest: ComparisonFile[] = [
  {
    filename: 'BillingTests.cs',
    code: withoutTest,
    scope: 'test',
    note: 'The fixture wires up the app, the client, authentication and cleanup itself — and the next fixture will do it again.',
    infrastructureLines: [
      1, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 16, 17, 18, 20, 21, 22, 23, 24, 26, 27, 28, 29, 31,
      32, 33, 34, 35, 36, 38, 39, 43, 50, 52, 53, 58, 64, 66, 67, 73, 74, 75,
    ],
  },
  {
    filename: 'TestSupport.cs',
    code: withoutSupport,
    scope: 'suite',
    note: 'Provisioning helpers you write and maintain, including their own HttpClient lifetimes. None of it describes a scenario.',
    infrastructureLines: [
      1, 3, 5, 6, 7, 8, 9, 10, 12, 13, 14, 15, 16, 17, 19, 20, 21, 23, 24, 25, 26, 27, 28, 29, 31,
      32, 33, 34, 35, 36, 37, 38, 40, 41, 42, 43,
    ],
  },
  {
    filename: 'Dtos.cs',
    code: withoutDtos,
    scope: 'suite',
    note: 'Response models that exist only so the JSON has somewhere to land.',
    infrastructureLines: [1, 3, 5, 6, 8, 9, 11, 13],
  },
];

export const withProtoTest: ComparisonFile[] = [
  {
    filename: 'BillingTests.cs',
    code: withTest,
    scope: 'test',
    note: 'Three attributes replace the setup and teardown. Adding another fixture costs those three lines.',
    infrastructureLines: [1, 3, 4, 5, 6, 7, 12, 13, 17, 23, 28, 34, 37, 39, 40, 41, 42],
  },
  {
    filename: 'Setup.cs',
    code: withSetup,
    scope: 'suite',
    note: 'One host for the whole suite — and where the trace, the contract coverage and the HTML report come from.',
    infrastructureLines: [1, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 24],
  },
  {
    filename: 'Scenario.cs',
    code: withScenario,
    scope: 'suite',
    note: 'This is where the setup went: capabilities written once, then composed onto any test as attributes.',
    infrastructureLines: [
      1, 3, 4, 5, 6, 7, 9, 10, 13, 14, 26, 28, 29, 38, 39, 41, 42, 43, 44, 58, 59, 61, 62, 63, 64,
      65, 66, 74, 75,
    ],
  },
];
