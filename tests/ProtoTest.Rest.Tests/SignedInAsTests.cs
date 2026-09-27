namespace ProtoTest.Rest.Tests;

using System.Net;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Rest;

/// <summary>
/// The honest boundary of the built-in test user: the identity is only a request header, and only an
/// in-process application whose suite registered the app-side authentication reads it. Everywhere else
/// the shipped authenticator stays inert - the request is sent unchanged and the trace says why.
/// </summary>
[TestFixture]
public sealed class SignedInAsTests
{
    [Test]
    public async Task WithoutAnInProcessServer_ShouldLeaveTheRequestWithoutATestUser()
    {
        var requests = new List<HttpRequestMessage>();
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:App:BaseUrl"] = "https://example.test"
            }));
        builder.AddApplication("App", app => app.AddRest(rest => rest.AddClient(
            "Api",
            configure: http => http.ConfigurePrimaryHttpMessageHandler(() => new CapturingHandler(requests)))));
        await using var host = builder.Build();

        var method = GetCaseMethod();
        await host.StartTestAsync("published user", "30001", method, ProtoAttributeResolver.Resolve(method));
        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/resource");
            response.Should.HaveHttpStatus(HttpStatusCode.OK);
        }
        finally
        {
            await host.CompleteTestAsync();
        }

        var test = host.Trace.Snapshot().Tests.Single();
        var user = test.Entities!.Single(entity =>
            entity.Kind == ProtoTraceEntityKinds.Auth && entity.Id == "auth:user");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests.Single().Headers.Contains(ProtoTestUserHeader.HeaderName), Is.False,
                "a published application cannot honour the test user, so the header stays off the request");
            Assert.That(user.State["auth.transport"], Is.EqualTo("inert"));
            Assert.That(user.State["auth.reason"], Does.Contain("AddTestUserAuthentication"));
            Assert.That(test.Entries, Has.Some.Matches<ProtoTraceEntry>(entry =>
                entry.Kind == "auth.user.inert"));
        }
    }

    [Test]
    public async Task SignedInUser_ShouldBePerTestStateThatTheSuiteCanRead()
    {
        var requests = new List<HttpRequestMessage>();
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:App:BaseUrl"] = "https://example.test"
            }));
        builder.AddApplication("App", app => app.AddRest(rest => rest.AddClient(
            "Api",
            configure: http => http.ConfigurePrimaryHttpMessageHandler(() => new CapturingHandler(requests)))));
        await using var host = builder.Build();

        var method = GetCaseMethod();
        await host.StartTestAsync("read the user", "30002", method, ProtoAttributeResolver.Resolve(method));
        try
        {
            var user = Proto.Context.SignedInUser();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(user.Name, Is.EqualTo("grace"));
                Assert.That(user.Roles, Is.EqualTo(new[] { "reporter" }));
                Assert.That(user.Claims, Is.EqualTo(new[] { new ProtoTestUserClaim("tenant", "atlas") }));
            }

            Proto.Context.SignIn(new ProtoTestUser("henry", ["editor"]));
            Assert.That(Proto.Context.SignedInUser().Name, Is.EqualTo("henry"),
                "SignIn replaces the identity for the rest of the test");
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task ComposedAuthMetadata_ShouldReachTheAuthHookAndTheRequest()
    {
        var requests = new List<HttpRequestMessage>();
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:App:BaseUrl"] = "https://example.test"
            }));
        builder.AddApplication("App", app => app.AddRest(rest => rest.AddClient(
            "Api",
            configure: http => http.ConfigurePrimaryHttpMessageHandler(() => new CapturingHandler(requests)))));
        await using var host = builder.Build();

        var method = GetComposedCaseMethod();
        await host.StartTestAsync(
            "composed auth metadata", "30003", method, ProtoAttributeResolver.Resolve(method));
        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/resource");
            response.Should.HaveHttpStatus(HttpStatusCode.OK);
        }
        finally
        {
            await host.CompleteTestAsync();
        }

        var test = host.Trace.Snapshot().Tests.Single();
        var protocol = test.Entities!.Single(entity =>
            entity.Kind == ProtoTraceEntityKinds.Auth && entity.Id == "Rest");
        var user = test.Entities!.Single(entity =>
            entity.Kind == ProtoTraceEntityKinds.Auth && entity.Id == "auth:user");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                requests.Single().Headers.GetValues(HeaderAuthenticator.HeaderName),
                Is.EqualTo(new[] { "composed" }),
                "an authenticator the composite declared reached the request");
            Assert.That(protocol.State["auth.types"], Does.Contain(nameof(HeaderAuthenticator)));
            Assert.That(protocol.State["auth.types"], Does.Contain(nameof(SignedInAsAttribute)),
                "the composite's [SignedInAs] rides along the protocol's authenticator set");
            Assert.That(protocol.State["auth.source"], Is.EqualTo("class"));
            Assert.That(user.State["auth.user"], Is.EqualTo("grace"));
        }
    }

    private static MethodInfo GetCaseMethod()
        => typeof(PublishedUserCases).GetMethod(nameof(PublishedUserCases.SignedIn), BindingFlags.Instance | BindingFlags.Public)
           ?? throw new InvalidOperationException("The test case method was not found.");

    private static MethodInfo GetComposedCaseMethod()
        => typeof(ComposedUserCases).GetMethod(nameof(ComposedUserCases.SignedIn), BindingFlags.Instance | BindingFlags.Public)
           ?? throw new InvalidOperationException("The composed test case method was not found.");

    [Application("App", "Rest:Api")]
    public sealed class PublishedUserCases
    {
        [SignedInAs("grace", "reporter", Claims = ["tenant=atlas"])]
        public void SignedIn()
        {
        }
    }

    [Application("App", "Rest:Api")]
    [MemberComposite("grace", "reporter")]
    public sealed class ComposedUserCases
    {
        public void SignedIn()
        {
        }
    }

    /// <summary>
    /// The group a composed user is: the identity declaration and the authenticator that carries it.
    /// The composite composes a lifecycle attribute and a metadata-only one, so it proves both kinds
    /// reach the resolution the lifecycle and the auth hook share.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    private sealed class MemberCompositeAttribute(string name, params string[] roles) : ProtoCompositeAttribute
    {
        protected override IReadOnlyList<Attribute> Compose() =>
        [
            new AuthAttribute<HeaderAuthenticator>(),
            new SignedInAsAttribute(name, roles)
        ];
    }

    private sealed class HeaderAuthenticator : IProtoHttpAuthenticator
    {
        public const string HeaderName = "X-Composed-Auth";

        public ValueTask AuthenticateAsync(
            ProtoHttpAuthenticationContext context,
            CancellationToken cancellationToken = default)
        {
            context.Request.Headers.TryAddWithoutValidation(HeaderName, "composed");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingHandler(List<HttpRequestMessage> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok")
            });
        }
    }
}
