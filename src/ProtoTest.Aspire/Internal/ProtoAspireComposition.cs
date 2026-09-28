namespace ProtoTest.Aspire.Internal;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// The one Aspire composition of a host: the AppHost pieces a run declared, keyed by entry point, and
/// the resource registry the test-side accessor reads. It is a service of the host builder, so both
/// the host-level <c>AddAspireAppHost</c> call and an integration reached from inside
/// <c>AddApplication</c> (a provider chain) resolve the same pieces.
/// </summary>
internal sealed class ProtoAspireComposition
{
    public Dictionary<Type, IProtoAspireAppHost> Pieces { get; } = [];

    public ProtoAspireRegistry Registry { get; } = new();

    public bool RegistryRegistered { get; set; }

    /// <summary>Returns the host's composition, creating it into the collection on first use.</summary>
    public static ProtoAspireComposition For(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(ProtoAspireComposition)
                && descriptor.ImplementationInstance is ProtoAspireComposition existing)
            {
                return existing;
            }
        }

        var created = new ProtoAspireComposition();
        services.AddSingleton(created);
        return created;
    }

    /// <summary>Returns the AppHost piece for an entry point, creating it for one resource on first use.</summary>
    public ProtoAspireAppHost<TEntryPoint> PieceFor<TEntryPoint>(string resource)
        where TEntryPoint : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        if (Pieces.TryGetValue(typeof(TEntryPoint), out var existing))
        {
            return (ProtoAspireAppHost<TEntryPoint>)existing;
        }

        var piece = new ProtoAspireAppHost<TEntryPoint>([resource], configure: null, publishEndpoints: false);
        Pieces[typeof(TEntryPoint)] = piece;
        return piece;
    }

    /// <summary>Registers the resource-to-key lookup into the host, once.</summary>
    public void RegisterRegistry(IProtoHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (RegistryRegistered)
        {
            return;
        }

        builder.ConfigureServices(services => services.AddSingleton(Registry));
        RegistryRegistered = true;
    }
}

/// <summary>
/// The non-generic view of an AppHost piece a host-level call needs: its identity, its resources,
/// the keys its publish mappings fill and the mappings a <c>MapConnectionString</c> adds after the
/// piece was created.
/// </summary>
internal interface IProtoAspireAppHost : IProtoInfrastructure
{
    IReadOnlyList<string> Resources { get; }

    /// <summary>Gets every key the AppHost's publish mappings fill.</summary>
    IReadOnlyList<string> PublishKeys { get; }

    void AddPublish(string resource, string key, ProtoAspirePublishKind kind, bool replaceEndpoints = false);
}
