import type {ComparisonConcern, ComparisonFile} from '@site/src/components/Comparison';

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

        created.ShouldHaveHttpStatus(HttpStatusCode.Created).ShouldMatchShape(new
        {
            product = "observability-seat",
            quantity = 12,
            total = JsonValue.GreaterThan(200m),
            status = "pending"
        });

        using var invoices = await Proto.Context.Rest()
            .GetAsync("/api/billing/invoices", new { state = "open" });

        invoices.ShouldHaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
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
                .AddCollector<RestCoverageCollector>()
                .AddCollector<OpenApiCoverageCollector>())
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
            .ShouldHaveHttpStatus(HttpStatusCode.Created)
            .ReadAsJson<EnvironmentResponse>()!;

        context.SetContext(new SampleEnvironmentContext(
            environment.Tenant, environment.Name, environment.ApiBaseUrl));
    }

    public override async Task AfterTestAsync(ProtoExecutionContext context)
    {
        var environment = context.TryResolve<SampleEnvironmentContext>();
        if (environment is null) return;

        using var response = await context.Rest(SampleAppTargets.Api)
            .WithoutAuth()
            .DeleteAsync("/test-support/environments/{tenant}",
                new { environment.Tenant });
        response.ShouldHaveHttpStatus(HttpStatusCode.NoContent);
    }
}

public sealed class SampleUserAttribute(string role) : ProtoAttribute
{
    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var environment = context.Resolve<SampleEnvironmentContext>();
        using var response = await context.Rest(SampleAppTargets.Api)
            .WithoutAuth()
            .Body(new CreateUserRequest($"{role}.{context.TestId}@example.test", role))
            .PostAsync("/test-support/environments/{tenant}/users",
                new { environment.Tenant });

        var user = response
            .ShouldHaveHttpStatus(HttpStatusCode.Created)
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
        var environment = context.Test.Resolve<SampleEnvironmentContext>();
        var user = context.Test.Resolve<SampleUserContext>();

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
      32, 33, 34, 35, 36, 38, 39, 50, 64, 75,
    ],
    folds: [
      {
        line: 23,
        label: 'Environments.CreateAsync',
        source: 'TestSupport.cs',
        from: 12,
        to: 21,
        summary: 'Creates a tenant by calling the app’s test-support endpoint.',
        reuse: 'called from every fixture',
      },
      {
        line: 24,
        label: 'Users.CreateAsync',
        source: 'TestSupport.cs',
        from: 33,
        to: 42,
        summary: 'Creates a user over HTTP, with a client of its own.',
        reuse: 'called from every fixture',
      },
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
    infrastructureLines: [1, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 15, 42],
    folds: [
      {
        line: 9,
        label: '[RestClient]',
        source: 'Setup.cs',
        from: 15,
        to: 18,
        summary: 'Registers the client and its collectors once, for the whole suite.',
        reuse: 'written once',
      },
      {
        line: 10,
        label: '[SampleEnvironment]',
        source: 'Scenario.cs',
        from: 9,
        to: 39,
        summary: 'Provisions a tenant before the test and deletes it in teardown.',
        reuse: 'composed onto any test',
      },
      {
        line: 11,
        label: '[Auth<SampleUserAuthenticator>]',
        source: 'Scenario.cs',
        from: 61,
        to: 75,
        summary: 'Attaches the signed-in user to every request.',
        reuse: 'composed onto any test',
      },
      {
        line: 15,
        label: '[SampleUser]',
        source: 'Scenario.cs',
        from: 41,
        to: 59,
        summary: 'Creates a user with the role this test asks for.',
        reuse: 'composed onto any test',
      },
    ],
  },
  {
    filename: 'Setup.cs',
    code: withSetup,
    scope: 'suite',
    note: 'One host for the whole suite — and where the trace, the contract coverage and the HTML report come from.',
    infrastructureLines: [
      1, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24,
    ],
  },
  {
    filename: 'Scenario.cs',
    code: withScenario,
    scope: 'suite',
    note: 'This is where the setup went: capabilities written once, then composed onto any test as attributes.',
    infrastructureLines: [
      1, 3, 4, 5, 6, 7, 9, 10, 11, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 28, 29,
      30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53,
      54, 55, 56, 57, 58, 59, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75,
    ],
  },
];

/**
 * The same comparison, task by task: what a fixture has to do, the lines each side spends on it and where
 * those lines live. Ranges are 1-based and inclusive, into the files above; the fixture file of each side is
 * what a new test pays again, every other file is written once.
 */
export const comparisonConcerns: ComparisonConcern[] = [
  {
    id: 'host',
    task: 'Run the application in-process',
    without: {
      summary: 'A WebApplicationFactory per fixture, created and disposed by hand.',
      slices: [{file: 'BillingTests.cs', ranges: [[11, 11], [16, 18], [38, 39]]}],
    },
    with: {
      home: 'suite host',
      summary: 'Registered once for the suite. Every test gets the running app.',
      slices: [{file: 'Setup.cs', ranges: [[19, 19]]}],
    },
  },
  {
    id: 'tenant',
    task: 'A tenant of its own',
    without: {
      summary: 'A field, a SetUp call and an HTTP helper with its own client.',
      slices: [
        {file: 'BillingTests.cs', ranges: [[13, 13], [23, 23]]},
        {file: 'TestSupport.cs', ranges: [[12, 21]]},
      ],
    },
    with: {
      home: 'attribute',
      summary: '[SampleEnvironment] provisions it before the test and hands it to the context.',
      slices: [
        {file: 'BillingTests.cs', ranges: [[10, 10]]},
        {file: 'Scenario.cs', ranges: [[9, 26]]},
      ],
    },
  },
  {
    id: 'user',
    task: 'A user with the right role',
    without: {
      summary: 'Created in SetUp, so every test in the fixture gets the same role.',
      slices: [
        {file: 'BillingTests.cs', ranges: [[14, 14], [24, 24]]},
        {file: 'TestSupport.cs', ranges: [[33, 42]]},
      ],
    },
    with: {
      home: 'attribute',
      summary: 'The role is declared on the test that needs it.',
      slices: [
        {file: 'BillingTests.cs', ranges: [[15, 15]]},
        {file: 'Scenario.cs', ranges: [[41, 59]]},
      ],
    },
  },
  {
    id: 'auth',
    task: 'Authenticate every request',
    without: {
      summary: 'Default headers on one HttpClient shared by the whole fixture.',
      slices: [{file: 'BillingTests.cs', ranges: [[12, 12], [26, 28]]}],
    },
    with: {
      home: 'authenticator',
      summary: 'Applied per request from the test’s own context, so parallel tests never share a header.',
      slices: [
        {file: 'BillingTests.cs', ranges: [[9, 9], [11, 11]]},
        {file: 'Scenario.cs', ranges: [[61, 75]]},
      ],
    },
  },
  {
    id: 'calls',
    task: 'Make the calls',
    without: {
      summary: 'HttpClient calls, with the query string written into the route.',
      slices: [{file: 'BillingTests.cs', ranges: [[44, 46], [60, 60]]}],
    },
    with: {
      home: 'the test',
      summary: 'The fluent client. Routes, parameters and bodies are recorded in the trace.',
      slices: [{file: 'BillingTests.cs', ranges: [[18, 20], [30, 31]]}],
    },
  },
  {
    id: 'assert',
    task: 'Check the responses',
    without: {
      summary: 'Deserialize into DTOs written for the purpose, then assert field by field.',
      slices: [
        {file: 'BillingTests.cs', ranges: [[48, 48], [50, 58], [62, 62], [64, 73]]},
        {file: 'Dtos.cs', ranges: [[3, 13]]},
      ],
    },
    with: {
      home: 'the test',
      summary: 'The expected shape is the assertion. No DTOs, and a mismatch lists every property that differs.',
      slices: [{file: 'BillingTests.cs', ranges: [[22, 28], [33, 40]]}],
    },
  },
  {
    id: 'cleanup',
    task: 'Clean up, even when the test fails',
    without: {
      summary: 'TearDown disposes the client and deletes the tenant — if SetUp got that far.',
      slices: [
        {file: 'BillingTests.cs', ranges: [[31, 36]]},
        {file: 'TestSupport.cs', ranges: [[23, 28]]},
      ],
    },
    with: {
      home: 'attribute teardown',
      summary: 'The attribute that created the tenant deletes it. Teardown runs in reverse order and is traced.',
      slices: [{file: 'Scenario.cs', ranges: [[28, 38]]}],
    },
  },
  {
    id: 'diagnose',
    task: 'Find out what happened',
    without: {
      summary: 'Whatever the assertion message says, and the console output.',
      slices: [],
    },
    with: {
      home: 'suite host',
      summary: 'Every step, request and assertion lands in a .prototrace and an HTML report, with contract coverage.',
      slices: [{file: 'Setup.cs', ranges: [[17, 23]]}],
    },
  },
];
