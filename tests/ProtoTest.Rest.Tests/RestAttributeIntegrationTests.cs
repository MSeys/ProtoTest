namespace ProtoTest.Rest.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Http.Authenticators;

[TestFixture]
public sealed class RestAttributeIntegrationTests
{
    [TestCase(nameof(RestAttributeCases.ClassOnly), "Orders", "class-token")]
    [TestCase(nameof(RestAttributeCases.MethodClientOverride), "Inventory", "class-token")]
    [TestCase(nameof(RestAttributeCases.MethodAuthOverride), "Orders", "method-token")]
    [TestCase(nameof(RestAttributeCases.MethodClientAndAuthOverride), "Inventory", "method-token")]
    public async Task ClassAndMethodAttributes_ShouldComposeIndependently(
        string methodName,
        string expectedClient,
        string expectedToken)
    {
        var requests = new List<CapturedRequest>();
        await using var host = CreateHost(requests, "Orders", "Inventory");
        var method = GetCaseMethod<RestAttributeCases>(methodName);

        await host.StartTestAsync(methodName, "10001", method);

        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/resource");
            response.ShouldHaveHttpStatus(HttpStatusCode.OK);

            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(requests[0].ClientName, Is.EqualTo(expectedClient));
                Assert.That(requests[0].Authorization,
                    Is.EqualTo(new AuthenticationHeaderValue("Bearer", expectedToken)));
                var entries = host.Trace.Snapshot().Tests.Single().Entries;
                var request = entries.Single(entry => entry.Kind == "http.request");
                Assert.That(entries, Has.Some.Matches<ProtoTraceEntry>(entry =>
                    entry.Kind == "auth.apply" && entry.ParentId == request.Id));
                Assert.That(entries, Has.Some.Matches<ProtoTraceEntry>(entry =>
                    entry.Kind == "assert.http.status" && entry.ParentId == request.Id));
                Assert.That(entries.SelectMany(entry => entry.Attributes.Values),
                    Has.None.EqualTo(expectedToken));
            });
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task MissingAttributes_ShouldUseUnauthenticatedDefaultClient()
    {
        var requests = new List<CapturedRequest>();
        await using var host = CreateHost(requests, "Default");
        var method = GetCaseMethod<DefaultRestAttributeCases>(nameof(DefaultRestAttributeCases.NoAttributes));

        await host.StartTestAsync(method.Name, "10002", method);

        try
        {
            await Proto.Context.Rest().GetAsync("/resource");

            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(requests[0].ClientName, Is.EqualTo("Default"));
                Assert.That(requests[0].Authorization, Is.Null);
            });
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task InheritedClassAttributes_ShouldApplyToDerivedTestClass()
    {
        var requests = new List<CapturedRequest>();
        await using var host = CreateHost(requests, "Orders");
        var method = GetCaseMethod<InheritedRestAttributeCases>(nameof(InheritedRestAttributeCases.Inherited));

        await host.StartTestAsync(method.Name, "10003", method);

        try
        {
            await Proto.Context.Rest().GetAsync("/resource");

            Assert.That(requests.Single(), Is.EqualTo(
                new CapturedRequest("Orders", new AuthenticationHeaderValue("Bearer", "base-token"))));
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task RestHook_ShouldRunBeforeProtoAttributes_RegardlessOfAttributeOrder()
    {
        var requests = new List<CapturedRequest>();
        await using var host = CreateHost(requests, "Orders", "Inventory");
        var method = GetCaseMethod<RestAttributeCases>(nameof(RestAttributeCases.WithOrderedProtoAttributes));
        var attributes = GetProtoAttributes(method);

        await host.StartTestAsync(method.Name, "10004", method, attributes);
        var state = Proto.Context.Resolve<AttributeOrderState>();

        try
        {
            await Proto.Context.Rest().GetAsync("/resource");
            state.Events.Add("Test");
        }
        finally
        {
            await host.CompleteTestAsync();
        }

        Assert.That(state.Events, Is.EqualTo(new[]
        {
            "Method:Before:Inventory",
            "Class:Before:Inventory",
            "Test",
            "Class:After:Inventory",
            "Method:After:Inventory"
        }));
        Assert.That(requests, Has.Count.EqualTo(3));
        Assert.That(requests.Select(request => request.ClientName),
            Is.All.EqualTo("Inventory"));
        Assert.That(requests.Select(request => request.Authorization),
            Is.All.EqualTo(new AuthenticationHeaderValue("Bearer", "method-token")));
    }

    private static ProtoHost CreateHost(List<CapturedRequest> requests, params string[] clientNames)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ProtoTest:Applications:App:BaseUrl"] = "https://example.test" }));
        builder.AddApplication("App", app => app.AddRest(rest =>
        {
            foreach (var clientName in clientNames)
            {
                rest.AddClient(clientName, configure: http =>
                    http.ConfigurePrimaryHttpMessageHandler(() => new CapturingHandler(clientName, requests)));
            }
        }));
        // A host-level default for the case with no [Application].
        builder.ConfigureServices(services =>
            services.AddSingleton<IProtoClientInitializer>(new CapturingRestClientInitializer("Default", requests)));
        return builder.Build();
    }

    private static MethodInfo GetCaseMethod<T>(string methodName)
        => typeof(T).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)
           ?? throw new InvalidOperationException($"Test case method '{methodName}' was not found.");

    private static IReadOnlyList<ProtoAttribute> GetProtoAttributes(MethodInfo method)
        => ProtoAttributeResolver.Resolve(method);

    [Application("App", "Rest:Orders")]
    [RestAuth<BearerTokenAuthenticator>("class-token")]
    [RestStateTracking("Class", Order = 20)]
    private sealed class RestAttributeCases
    {
        public void ClassOnly() { }

        [Application("App", "Rest:Inventory")]
        public void MethodClientOverride() { }

        [RestAuth<BearerTokenAuthenticator>("method-token")]
        public void MethodAuthOverride() { }

        [RestAuth<BearerTokenAuthenticator>("method-token")]
        [Application("App", "Rest:Inventory")]
        public void MethodClientAndAuthOverride() { }

        [RestStateTracking("Method", Order = 10)]
        [RestAuth<BearerTokenAuthenticator>("method-token")]
        [Application("App", "Rest:Inventory")]
        public void WithOrderedProtoAttributes() { }
    }

    private sealed class DefaultRestAttributeCases
    {
        public void NoAttributes() { }
    }

    [Application("App", "Rest:Orders")]
    [RestAuth<BearerTokenAuthenticator>("base-token")]
    private abstract class RestAttributeBase
    {
    }

    private sealed class InheritedRestAttributeCases : RestAttributeBase
    {
        public void Inherited() { }
    }

    private sealed class RestStateTrackingAttribute(string name) : ProtoAttribute
    {
        public override async Task BeforeTestAsync(ProtoExecutionContext context)
        {
            var state = context.TryResolve<AttributeOrderState>();
            if (state is null)
            {
                state = new AttributeOrderState();
                context.SetContext(state);
            }

            var response = await context.Rest().GetAsync($"/attribute/{name}");
            state.SelectedClientName = response.Content;
            state.Events.Add($"{name}:Before:{state.SelectedClientName}");
        }

        public override Task AfterTestAsync(ProtoExecutionContext context)
        {
            var state = context.Resolve<AttributeOrderState>();
            state.Events.Add($"{name}:After:{state.SelectedClientName}");
            return Task.CompletedTask;
        }
    }

    private sealed class AttributeOrderState : IProtoContext
    {
        public List<string> Events { get; } = [];
        public string SelectedClientName { get; set; } = string.Empty;
    }

    private sealed class CapturingRestClientInitializer(string name, List<CapturedRequest> requests)
        : IProtoClientInitializer<HttpClient>
    {
        public string Name { get; } = name;

        public Task<bool> TryInitializeAsync(
            ProtoExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            var client = new HttpClient(new CapturingHandler(Name, requests))
            {
                BaseAddress = new Uri("https://example.test")
            };

            context.RegisterClient(client, Name);
            return Task.FromResult(true);
        }
    }

    private sealed class CapturingHandler(string clientName, List<CapturedRequest> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requests.Add(new CapturedRequest(clientName, request.Headers.Authorization));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(clientName)
            });
        }
    }

    private sealed record CapturedRequest(string ClientName, AuthenticationHeaderValue? Authorization);
}
