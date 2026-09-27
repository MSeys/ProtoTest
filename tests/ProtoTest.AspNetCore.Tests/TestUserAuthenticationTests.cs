namespace ProtoTest.AspNetCore.Tests;

using System.Net;
using System.Reflection;
using System.Text;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Http.Authenticators;
using ProtoTest.Rest;

/// <summary>
/// The built-in test user end to end: a test declares an identity with claims and roles, the app-side
/// authentication turns it into the application's principal, and the application's own authorization
/// decides. The identity is per-test state and reaches the application only while it runs in-process.
/// </summary>
[TestFixture]
public sealed class TestUserAuthenticationTests
{
    private static readonly MethodInfo AliceMethod = GetCaseMethod(nameof(Alice));
    private static readonly MethodInfo BobMethod = GetCaseMethod(nameof(Bob));
    private static readonly MethodInfo AnonymousMethod = GetCaseMethod(nameof(Anonymous));
    private static readonly MethodInfo WithBearerTokenMethod = GetCaseMethod(nameof(WithBearerToken));

    [Test]
    public async Task SignedInUser_ShouldReachTheApplicationWithClaimsAndRoles()
    {
        await using var host = CreateHost();
        await host.StartTestAsync("alice reaches the app", "20001", AliceMethod, ProtoAttributeResolver.Resolve(AliceMethod));

        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/auth/me");
            response.Should.HaveHttpStatus(HttpStatusCode.OK);
            var identity = response.ReadRequired<SampleApi.AuthIdentity>();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(identity.Authenticated, Is.True);
                Assert.That(identity.Name, Is.EqualTo("alice"));
                Assert.That(identity.Roles, Is.EqualTo(new[] { "admin", "billing" }));
                Assert.That(identity.Claims, Does.Contain(new SampleApi.AuthClaim("tenant", "northstar")));
            }
        }
        finally
        {
            await host.CompleteTestAsync();
        }

        var test = host.Trace.Snapshot().Tests.Single();
        var user = test.Entities!.Single(entity =>
            entity.Kind == ProtoTraceEntityKinds.Auth && entity.Id == "auth:user");
        var protocol = test.Entities!.Single(entity =>
            entity.Kind == ProtoTraceEntityKinds.Auth && entity.Id == "Rest");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.State["auth.user"], Is.EqualTo("alice"));
            Assert.That(user.State["auth.roles"], Is.EqualTo("admin, billing"));
            Assert.That(user.State["auth.claim_types"], Is.EqualTo("tenant"));
            Assert.That(user.State["auth.transport"], Is.EqualTo("in-process"));
            Assert.That(protocol.State["auth.types"], Does.Contain("SignedInAsAttribute"),
                "the protocol's auth entity names the declaration among its authenticators");
            Assert.That(test.Entries, Has.Some.Matches<ProtoTraceEntry>(entry =>
                entry.Kind == "auth.user.sign-in"));
            Assert.That(test.Entries, Has.Some.Matches<ProtoTraceEntry>(entry =>
                entry.Kind == "http.request" && entry.Attributes["auth.outcome"] == "applied"));
        }
    }

    [Test]
    public async Task Roles_ShouldEnforceTheApplicationsOwnAuthorization()
    {
        await using var host = CreateHost();
        await host.StartTestAsync("bob hits an admin endpoint", "20002", BobMethod, ProtoAttributeResolver.Resolve(BobMethod));

        try
        {
            using var denied = await Proto.Context.Rest().GetAsync("/auth/admin");
            denied.Should.HaveHttpStatus(HttpStatusCode.Forbidden);

            // The context API re-signs the same test in as another identity, so a role the test
            // declares later is what the application authorizes against.
            Proto.Context.SignIn(new ProtoTestUser("carol", ["admin"]));
            using var allowed = await Proto.Context.Rest().GetAsync("/auth/admin");
            allowed.Should.HaveHttpStatus(HttpStatusCode.OK);
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task AnonymousRequests_ShouldStayAnonymous()
    {
        await using var host = CreateHost();
        await host.StartTestAsync("no test user", "20003", AnonymousMethod, ProtoAttributeResolver.Resolve(AnonymousMethod));

        try
        {
            using var me = await Proto.Context.Rest().GetAsync("/auth/me");
            me.Should.HaveHttpStatus(HttpStatusCode.OK);
            Assert.That(me.ReadRequired<SampleApi.AuthIdentity>().Authenticated, Is.False);

            using var admin = await Proto.Context.Rest().GetAsync("/auth/admin");
            admin.Should.HaveHttpStatus(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task TestUser_ShouldNotLeakIntoTheNextTest()
    {
        await using var host = CreateHost();
        await host.StartTestAsync("alice first", "20004", AliceMethod, ProtoAttributeResolver.Resolve(AliceMethod));
        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/auth/me");
            Assert.That(response.ReadRequired<SampleApi.AuthIdentity>().Name, Is.EqualTo("alice"));
        }
        finally
        {
            await host.CompleteTestAsync();
        }

        await host.StartTestAsync("bob second", "20005", BobMethod, ProtoAttributeResolver.Resolve(BobMethod));
        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/auth/me");
            var identity = response.ReadRequired<SampleApi.AuthIdentity>();
            Assert.That(identity.Name, Is.EqualTo("bob"), "the second test starts with its own identity");
            Assert.That(identity.Roles, Is.EqualTo(new[] { "viewer" }));
        }
        finally
        {
            await host.CompleteTestAsync();
        }

        await host.StartTestAsync("nobody third", "20006", AnonymousMethod, ProtoAttributeResolver.Resolve(AnonymousMethod));
        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/auth/me");
            Assert.That(response.ReadRequired<SampleApi.AuthIdentity>().Authenticated, Is.False,
                "the third test starts with no identity");
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    /// <summary>
    /// The identity reaches the application through the same composite the other authenticators ride;
    /// <c>[Auth]</c> and <c>[SignedInAs]</c> compose instead of replacing each other.
    /// </summary>
    [Test]
    public async Task TestUser_ShouldComposeWithTheExistingAuthenticators()
    {
        await using var host = CreateHost();
        await host.StartTestAsync(
            "composed auth",
            "20007",
            WithBearerTokenMethod,
            ProtoAttributeResolver.Resolve(WithBearerTokenMethod));

        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/auth/me");
            Assert.That(response.ReadRequired<SampleApi.AuthIdentity>().Name, Is.EqualTo("dana"));
        }
        finally
        {
            await host.CompleteTestAsync();
        }

        var test = host.Trace.Snapshot().Tests.Single();
        Assert.That(
            test.Entries.Count(entry => entry.Kind == "auth.handler.apply"),
            Is.EqualTo(2),
            "the bearer authenticator and the test user both applied");
    }

    /// <summary>
    /// The sample's shape: the class owns the authenticator, the method only names the user. The
    /// method-level identity must not replace the class-level authenticator.
    /// </summary>
    [Test]
    public async Task TestUser_ShouldRideAlongAClassLevelAuthenticator()
    {
        await using var host = CreateHost();
        var method = ClassAuthenticatedCases.InheritedAuthMethod;
        await host.StartTestAsync("class auth with a test user", "20008", method, ProtoAttributeResolver.Resolve(method));

        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/auth/me");
            Assert.That(response.ReadRequired<SampleApi.AuthIdentity>().Name, Is.EqualTo("erin"));
        }
        finally
        {
            await host.CompleteTestAsync();
        }

        var test = host.Trace.Snapshot().Tests.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                test.Entries.Count(entry => entry.Kind == "auth.handler.apply"),
                Is.EqualTo(2),
                "the class-level bearer authenticator was not replaced by the method-level test user");
            var request = test.Entries.Single(entry => entry.Kind == "http.request");
            Assert.That(request.Attributes["auth.outcome"], Is.EqualTo("applied"));
        }
    }

    /// <summary>
    /// The header is not a credential: an in-process application that never registers the app-side
    /// authentication ignores it and stays anonymous.
    /// </summary>
    [Test]
    public async Task WithoutAppSideAuthentication_ShouldLeaveTheApplicationAnonymous()
    {
        await using var host = new ProtoHostBuilder()
            .AddApplication("Api", app => app
                .AddAspNetCoreServer<SampleApi.Program>()
                .AddRest(rest => rest.AddClient("Api")))
            .Build();
        await host.StartTestAsync(
            "in-process without the app-side handler",
            "20009",
            AliceMethod,
            ProtoAttributeResolver.Resolve(AliceMethod));

        try
        {
            using var response = await Proto.Context.Rest().GetAsync("/auth/me");
            Assert.That(
                response.ReadRequired<SampleApi.AuthIdentity>().Authenticated,
                Is.False,
                "the application must register webHost.AddTestUserAuthentication() to honour the header");
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    /// <summary>
    /// A malformed header - not Base64, a null role, or an oversized identity - fails authentication,
    /// so the application stays anonymous and its authorization challenges normally instead of the
    /// request failing with a server error.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(MalformedHeaders))]
    public async Task MalformedHeader_ShouldStayAnonymousWithoutAServerError(string header)
    {
        await using var host = CreateHost();
        await host.StartTestAsync(
            "malformed test user",
            "20010",
            AnonymousMethod,
            ProtoAttributeResolver.Resolve(AnonymousMethod));

        try
        {
            using var me = await Proto.Context.Rest()
                .Header(ProtoTestUserHeader.HeaderName, header)
                .GetAsync("/auth/me");
            Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(
                me.ReadRequired<SampleApi.AuthIdentity>().Authenticated,
                Is.False,
                "a malformed identity must not authenticate");

            using var admin = await Proto.Context.Rest()
                .Header(ProtoTestUserHeader.HeaderName, header)
                .GetAsync("/auth/admin");
            admin.Should.HaveHttpStatus(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    private static IEnumerable<string> MalformedHeaders()
    {
        yield return "not base64!";
        yield return Convert.ToBase64String(Encoding.UTF8.GetBytes(
            """{"Name":"alice","Roles":["admin",null],"Claims":[]}"""));
        yield return ProtoTestUserHeader.Encode(new ProtoTestUser(new string('a', 20_000)));
    }

    private static ProtoHost CreateHost()
        => new ProtoHostBuilder()
            .AddApplication("Api", app => app
                .AddAspNetCoreServer<SampleApi.Program>(webHost => webHost.AddTestUserAuthentication())
                .AddRest(rest => rest.AddClient("Api")))
            .Build();

    private static MethodInfo GetCaseMethod(string name)
        => typeof(TestUserAuthenticationTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new InvalidOperationException($"The test case method '{name}' was not found.");

    [Application("Api")]
    [SignedInAs("alice", "admin", "billing", Claims = ["tenant=northstar"])]
    private static void Alice()
    {
    }

    [Application("Api")]
    [SignedInAs("bob", "viewer")]
    private static void Bob()
    {
    }

    [Application("Api")]
    private static void Anonymous()
    {
    }

    [Application("Api")]
    [SignedInAs("dana")]
    [Auth<BearerTokenAuthenticator>("dana-token")]
    private static void WithBearerToken()
    {
    }

    [Application("Api")]
    [Auth<BearerTokenAuthenticator>("class-token")]
    private abstract class ClassAuthenticatedCases
    {
        public static readonly MethodInfo InheritedAuthMethod =
            typeof(ClassAuthenticatedCases).GetMethod(nameof(InheritedAuth), BindingFlags.Public | BindingFlags.Static)!;

        [SignedInAs("erin")]
        public static void InheritedAuth()
        {
        }
    }
}
