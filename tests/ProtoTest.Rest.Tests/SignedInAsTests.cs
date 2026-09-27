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

    private static MethodInfo GetCaseMethod()
        => typeof(PublishedUserCases).GetMethod(nameof(PublishedUserCases.SignedIn), BindingFlags.Instance | BindingFlags.Public)
           ?? throw new InvalidOperationException("The test case method was not found.");

    [Application("App", "Rest:Api")]
    public sealed class PublishedUserCases
    {
        [SignedInAs("grace", "reporter", Claims = ["tenant=atlas"])]
        public void SignedIn()
        {
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
