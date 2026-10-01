namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>
/// Declares the user a test signs in as, for example <c>[SignedInAs("alice", "admin", "billing")]</c>.
/// The identity is published to the test's execution context (read it with <c>context.SignedInUser()</c>)
/// and rides the existing HTTP auth lifecycle: REST, GraphQL and gRPC requests carry it to the
/// application, and the protocol's <c>Auth</c> entity names the declaration among its authenticators.
/// The shipped app-side authentication (<c>AddTestUserAuthentication</c> in <c>ProtoTest.AspNetCore</c>)
/// turns it into the application's <c>ClaimsPrincipal</c>; without an in-process application the
/// shipped transport stays inert, the request is sent unchanged and the <c>auth:user</c> entity records
/// the reason.
/// </summary>
/// <example>
/// <code>
/// [ProtoTest]
/// [SignedInAs("alice", "Administrator", Claims = new[] { "tenant=northstar" })]
/// public async Task AdministratorsCanCreateProjects() { ... }
/// </code>
/// </example>
/// <remarks>
/// The declaration is orthogonal to <c>[Auth&lt;T&gt;]</c> - it names who the test acts as, not how a
/// request is authenticated - so a method-level <c>[SignedInAs]</c> rides along with a class-level
/// authenticator instead of replacing it.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SignedInAsAttribute : ProtoAttribute, IProtoHttpAuthMetadata
{
    /// <summary>The name a bare <c>[SignedInAs]</c> signs in as.</summary>
    public const string DefaultName = "test-user";

    private string[] _claims = [];

    /// <summary>Declares the identity; roles follow the name.</summary>
    public SignedInAsAttribute(string name = DefaultName, params string[] roles)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A test user name is required.", nameof(name))
            : name;
        Roles = [.. roles.Select(role =>
            string.IsNullOrWhiteSpace(role)
                ? throw new ArgumentException("A test user role must not be blank.", nameof(roles))
                : role)];
        Order = -100;
    }

    /// <summary>Gets the user name.</summary>
    public string Name { get; }

    /// <summary>Gets the user's roles.</summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>
    /// Gets claims in <c>type=value</c> form, for example <c>new[] { "tenant=northstar" }</c>. The
    /// <c>auth:user</c> trace entity records only the claim types; embedded test source or captured
    /// content can still contain a value.
    /// </summary>
    public string[] Claims
    {
        get => _claims;
        init => _claims = [.. value.Select(claim =>
            claim is null || !claim.Contains('=', StringComparison.Ordinal) || claim.StartsWith('=')
                ? throw new ArgumentException(
                    $"The claim '{claim}' is not in 'type=value' form.", nameof(value))
                : claim)];
    }

    IReadOnlyList<string> IProtoHttpAuthMetadata.Protocols => [];

    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.SignIn(new ProtoTestUser(
            Name,
            Roles,
            [.. Claims.Select(ParseClaim)]));
        return Task.CompletedTask;
    }

    IProtoHttpAuthenticator IProtoHttpAuthMetadata.Create(ProtoExecutionContext context)
        => new TestUserAuthenticator();

    private static ProtoTestUserClaim ParseClaim(string claim)
    {
        var separator = claim.IndexOf('=', StringComparison.Ordinal);
        return new ProtoTestUserClaim(claim[..separator], claim[(separator + 1)..]);
    }
}

/// <summary>
/// Sends the test's signed-in user to the application as the <see cref="ProtoTestUserHeader.HeaderName"/>
/// header. The header is only meaningful to an application the run hosts in-process and whose suite
/// registered the shipped app-side authentication; against a published application the authenticator is
/// inert (the request is sent unchanged, and <c>SignIn</c> recorded the reason on the auth entity).
/// </summary>
public sealed class TestUserAuthenticator : IProtoHttpAuthenticator
{
    public ValueTask AuthenticateAsync(
        ProtoHttpAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = context.Test.SignedInUser();
        var application = ProtoTestUserExtensions.SelectedApplication(context.Test);
        if (Proto.Host.HasCapability(ProtoCapabilityKinds.Server, null, application))
        {
            context.Request.Headers.TryAddWithoutValidation(
                ProtoTestUserHeader.HeaderName,
                ProtoTestUserHeader.Encode(user));
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>Publishes and reads the test's signed-in user.</summary>
public static class ProtoTestUserExtensions
{
    private const string EntityId = "auth:user";

    /// <summary>
    /// Signs the test in as <paramref name="user"/>: later <c>SignedInUser()</c> reads, requests made
    /// through the HTTP clients (REST, GraphQL, gRPC) and the trace's auth entity all use it. The
    /// identity is per-test state, so the next test starts with none.
    /// </summary>
    public static void SignIn(this ProtoExecutionContext context, ProtoTestUser user)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(user);
        context.SetContext(user);

        var application = SelectedApplication(context);
        var inProcess = Proto.Host.HasCapability(ProtoCapabilityKinds.Server, null, application);
        var state = new Dictionary<string, string?>
        {
            ["auth.user"] = user.Name,
            ["auth.roles"] = string.Join(", ", user.Roles),
            ["auth.claim_types"] = string.Join(", ", user.Claims.Select(claim => claim.Type).Distinct(StringComparer.Ordinal)),
            ["auth.application"] = application,
            // The shipped transport only exists while the application runs in-process; a suite
            // authenticator that maps the identity itself is not reflected here.
            ["auth.transport"] = inProcess ? "in-process" : "inert"
        };
        if (!inProcess)
        {
            state["auth.reason"] =
                $"Application '{application}' is not hosted in-process in this run, so the shipped " +
                "test-user authentication cannot serve it. Register it with AddAspNetCoreServer and " +
                "add the app-side authentication with webHost.AddTestUserAuthentication() - a published " +
                "application holds no test user.";
        }

        context.Trace.SetEntityState(
            ProtoTraceEntityKinds.Auth,
            EntityId,
            "Test user",
            state,
            scope: context.TestName,
            change: "signed-in");
        context.Trace.WriteEvent(
            inProcess ? "auth.user.sign-in" : "auth.user.inert",
            inProcess ? $"Signed in as {user.Name}" : $"The shipped test-user transport is inert for '{user.Name}'",
            "ProtoTest.Http",
            ProtoTracePhase.Setup,
            inProcess ? ProtoTraceOutcome.Succeeded : ProtoTraceOutcome.Skipped,
            state,
            entityKind: ProtoTraceEntityKinds.Auth,
            entityId: EntityId);
    }

    /// <summary>Returns the user the test is signed in as.</summary>
    /// <exception cref="InvalidOperationException">The test has no signed-in user.</exception>
    public static ProtoTestUser SignedInUser(this ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TryResolve<ProtoTestUser>()
            ?? throw new InvalidOperationException(
                "This test has no signed-in user. Declare one with [SignedInAs] on the class or method, " +
                "or call context.SignIn(new ProtoTestUser(...)) before the request is sent.");
    }

    /// <summary>The application the test selected, or <c>Default</c> when it selected none.</summary>
    internal static string SelectedApplication(ProtoExecutionContext context)
        => context.TryResolve<ProtoApplicationState>()?.ApplicationName ?? "Default";
}
