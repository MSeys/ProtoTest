namespace ProtoTest.Core;

/// <summary>
/// The contract an adapter's assembly class implements so the host base can show that adapter's own
/// "not initialized" guidance. Adapters implement it explicitly, keeping it off their public surface.
/// </summary>
public interface IProtoTestAssemblyHost<TSelf> where TSelf : IProtoTestAssemblyHost<TSelf>
{
    /// <summary>Adapter-specific guidance appended to the "not initialized" error.</summary>
    static abstract string UninitializedHint { get; }
}

/// <summary>
/// The assembly-level host every test-framework adapter manages: one <see cref="ProtoHost"/> per test
/// assembly, started once from the framework's assembly hook and stopped from its teardown. The lifetime
/// is a static of the closed generic type, so two adapter assemblies loaded in one process each own
/// their host instead of sharing one.
/// </summary>
public abstract class ProtoTestAssemblyHost<TSelf>
    where TSelf : ProtoTestAssemblyHost<TSelf>, IProtoTestAssemblyHost<TSelf>
{
    private static readonly ProtoTestHostLifetime Lifetime = new(TSelf.UninitializedHint);

    /// <summary>Gets the initialized root host for this adapter assembly.</summary>
    /// <exception cref="InvalidOperationException">Thrown when accessed before initialization.</exception>
    public static ProtoHost Host => Lifetime.Host;

    /// <summary>Builds and starts the host. Throws if it has already been initialized.</summary>
    protected static Task StartAsync(Action<IProtoHostBuilder> configure) => Lifetime.StartAsync(configure);

    /// <summary>Stops and disposes the host, clearing it so later access reports "not initialized".</summary>
    protected static Task StopAsync() => Lifetime.StopAsync();
}
