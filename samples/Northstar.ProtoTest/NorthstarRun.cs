namespace Northstar.ProtoTest;

using Microsoft.Extensions.Configuration;

/// <summary>
/// The store and broker choices that shape one run: a configured address wins, then a container this
/// run owns, then the run-owned SQLite file. Keeping them in one place makes the setup read as a
/// composition of capabilities instead of a collection of related boolean checks.
/// </summary>
internal sealed class NorthstarRun
{
    private NorthstarRun(
        IConfiguration configuration,
        string? targetUrl,
        string? configuredStore,
        bool ownsPostgres,
        bool usesPostgres,
        string? ownedStorePath,
        string? configuredMessaging,
        bool ownsMessagingBroker)
    {
        Configuration = configuration;
        TargetUrl = targetUrl;
        ConfiguredStore = configuredStore;
        OwnsPostgres = ownsPostgres;
        UsesPostgres = usesPostgres;
        OwnedStorePath = ownedStorePath;
        ConfiguredMessaging = configuredMessaging;
        OwnsMessagingBroker = ownsMessagingBroker;
    }

    public IConfiguration Configuration { get; }

    public string? TargetUrl { get; }

    /// <summary>False when the suite points at a deployed application instead of hosting one.</summary>
    public bool RunsLocalApplications => string.IsNullOrWhiteSpace(TargetUrl);

    public string? ConfiguredStore { get; }

    public bool OwnsPostgres { get; }

    public bool UsesPostgres { get; }

    public string DatabaseProvider => UsesPostgres ? "postgres" : "sqlite";

    public string? OwnedStorePath { get; }

    /// <summary>The store both hosted instances use; null while a container fills it for the run.</summary>
    public string? DatabaseConnection => ConfiguredStore
        ?? (UsesPostgres || OwnedStorePath is null ? null : $"Data Source={OwnedStorePath}");

    public string? ConfiguredMessaging { get; }

    public bool OwnsMessagingBroker { get; }

    public bool UsesMessaging => OwnsMessagingBroker || !string.IsNullOrWhiteSpace(ConfiguredMessaging);

    /// <summary>Whether the suite can compose the domain over the store it shares with the application.</summary>
    public bool CanComposeDomain => RunsLocalApplications || ConfiguredStore is not null || UsesPostgres;

    public static NorthstarRun From(IConfiguration configuration)
    {
        var targetUrl = configuration["ProtoTest:TargetUrl"];
        var configuredStore = configuration.GetConnectionString("Northstar");
        var ownsPostgres = string.Equals(
            configuration["ProtoTest:Database"],
            "postgres",
            StringComparison.OrdinalIgnoreCase);
        var usesPostgres = ownsPostgres
            || IsPostgresStore(configuration["Database:Provider"], configuredStore);
        var ownedStorePath = !usesPostgres && configuredStore is null
            ? Path.GetFullPath(Path.Combine("TestResults", "Northstar.ProtoTest", "northstar.db"))
            : null;
        var configuredMessaging = configuration["ProtoTest:Messaging:RabbitMq:ConnectionString"];
        var ownsMessagingBroker = string.Equals(
            configuration["ProtoTest:Messaging:Broker"],
            "container",
            StringComparison.OrdinalIgnoreCase);

        return new NorthstarRun(
            configuration,
            targetUrl,
            configuredStore,
            ownsPostgres,
            usesPostgres,
            ownedStorePath,
            configuredMessaging,
            ownsMessagingBroker);
    }

    /// <summary>Deletes a previous run's SQLite file so every run starts from an empty store.</summary>
    public void PrepareOwnedStore()
    {
        if (OwnedStorePath is null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(OwnedStorePath)!);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = OwnedStorePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Whether the configured store is PostgreSQL; anything unclear stays SQLite.</summary>
    internal static bool IsPostgresStore(string? provider, string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(provider))
        {
            return string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase)
                || string.Equals(provider, "postgresql", StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        return connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);
    }
}
