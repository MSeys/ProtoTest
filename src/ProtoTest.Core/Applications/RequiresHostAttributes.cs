namespace ProtoTest.Core;

/// <summary>
/// Skips the test unless the run hosts the worker program <typeparamref name="TProgram"/> with
/// <c>AddWorkerHost&lt;TProgram&gt;()</c>. The check is the worker capability that registration
/// declares, keyed by the program assembly's name, so a typo in a string cannot turn a missing worker
/// into a plausible skip.
/// </summary>
/// <example>
/// <code>
/// [RequiresWorker&lt;BillingWorker&gt;]
/// public async Task AnEventBecomesAnInvoice() { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresWorkerAttribute<TProgram> : RequiresCapabilityAttribute
    where TProgram : class
{
    public RequiresWorkerAttribute()
        : base(ProtoCapabilityKinds.Worker)
    {
        CapabilityName = typeof(TProgram).Assembly.GetName().Name ?? typeof(TProgram).FullName!;
    }

    protected override string DefaultReason
        => $"This test requires the '{CapabilityName}' worker, which this run does not host. " +
           $"Add it with AddWorkerHost<{typeof(TProgram).Name}>().";
}

/// <summary>
/// Skips the test unless the run's clock is authoritative for the application under test: the winning
/// in-process application server and a hosted worker bridge the test clock into the process they serve.
/// A test that advances the clock against an application running in its own process - published,
/// container, AppHost or loopback - must carry the gate instead of asserting a time the application
/// never saw.
/// </summary>
/// <example>
/// <code>
/// [RequiresTestClock]
/// public async Task AChargePointBootsWithTheSuitesClock() { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresTestClockAttribute : RequiresCapabilityAttribute
{
    public RequiresTestClockAttribute()
        : base(ProtoCapabilityKinds.Clock)
    {
    }

    protected override string DefaultReason
        => "This test requires the test clock to be authoritative for the application under test, " +
           "which the environment does not host: it is served by a published, container, AppHost or " +
           "loopback provider instead of the in-process test server.";
}

/// <summary>
/// Skips the test unless the host is composed with the named ASP.NET Core server, for example the
/// instance registered by <c>AddAspNetCoreServer&lt;TProgram&gt;(name)</c>. The check addresses the
/// capability's <see cref="ProtoCapabilityDescriptor.Instance"/> - the server name - so a configured
/// address that drops one named server does not satisfy a gate for another.
/// </summary>
/// <example>
/// <code>
/// [RequiresServer("Api")]
/// public async Task ...() { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresServerAttribute : RequiresCapabilityAttribute
{
    public RequiresServerAttribute(string name)
        : base(ProtoCapabilityKinds.Server)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ServerName = name;
        CapabilityInstance = name;
    }

    /// <summary>The server name the test needs.</summary>
    public string ServerName { get; }

    protected override string DefaultReason
        => $"This test requires the '{ServerName}' server, which this host is not composed with. " +
           $"Add it with AddAspNetCoreServer<TProgram>(name: \"{ServerName}\").";
}

/// <summary>
/// Skips the test unless the suite declares the named application through <c>AddApplication</c>, so an
/// application-specific test reads as skipped when the suite was composed without it instead of failing
/// at the first accessor.
/// </summary>
/// <example>
/// <code>
/// [RequiresApplication("Api")]
/// public async Task ...() { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresApplicationAttribute : ProtoAttribute, IProtoSkipCondition
{
    public RequiresApplicationAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>The application name the test needs.</summary>
    public string Name { get; }

    /// <summary>The reason reported when the application is missing.</summary>
    public string? Reason { get; init; }

    public string? GetSkipReason(ProtoHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.HasApplication(Name) ? null : Reason ?? DefaultReason;
    }

    private string DefaultReason
        => $"This test requires the '{Name}' application, which this suite does not declare. " +
           $"Declare it with AddApplication(\"{Name}\", app => ...).";
}
