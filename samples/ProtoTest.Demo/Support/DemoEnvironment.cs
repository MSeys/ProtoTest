namespace ProtoTest.Demo;

using Microsoft.Extensions.Configuration;

/// <summary>
/// The deployment choices that shape a demo run. Keeping these derived values together makes the
/// setup read as a composition of capabilities instead of a collection of related boolean checks.
/// </summary>
internal sealed class DemoEnvironment
{
    private DemoEnvironment(
        IConfiguration configuration,
        string? targetUrl,
        string? configuredDatabase,
        bool ownsPostgres,
        bool usesPostgres,
        string? configuredMessaging,
        bool ownsMessagingBroker,
        string? ownedDatabasePath)
    {
        Configuration = configuration;
        TargetUrl = targetUrl;
        ConfiguredDatabase = configuredDatabase;
        OwnsPostgres = ownsPostgres;
        UsesPostgres = usesPostgres;
        ConfiguredMessaging = configuredMessaging;
        OwnsMessagingBroker = ownsMessagingBroker;
        OwnedDatabasePath = ownedDatabasePath;
    }

    public IConfiguration Configuration { get; }

    public string? TargetUrl { get; }

    public bool UsesLocalApplications => string.IsNullOrWhiteSpace(TargetUrl);

    public string? ConfiguredDatabase { get; }

    public bool OwnsPostgres { get; }

    public bool UsesPostgres { get; }

    public string DatabaseProvider => UsesPostgres ? "postgres" : "sqlite";

    public string? OwnedDatabasePath { get; }

    public string DatabaseConnection => ConfiguredDatabase ?? $"Data Source={OwnedDatabasePath}";

    public bool CanComposeDomain => UsesLocalApplications || ConfiguredDatabase is not null || UsesPostgres;

    public string? ConfiguredMessaging { get; }

    public bool OwnsMessagingBroker { get; }

    public bool UsesMessaging => OwnsMessagingBroker || !string.IsNullOrWhiteSpace(ConfiguredMessaging);

    public bool RunsStandaloneConsole => UsesLocalApplications && !OwnsPostgres;

    public static DemoEnvironment From(IConfiguration configuration)
    {
        var targetUrl = configuration["ProtoTest:TargetUrl"];
        var configuredDatabase = configuration.GetConnectionString("Northstar");
        var ownsPostgres = string.Equals(
            configuration["ProtoTest:Database"],
            "postgres",
            StringComparison.OrdinalIgnoreCase);
        var usesPostgres = ownsPostgres || IsPostgresStore(
            configuration["Database:Provider"], configuredDatabase);
        var configuredMessaging = configuration["ProtoTest:Messaging:RabbitMq:ConnectionString"];
        var ownsMessagingBroker = string.Equals(
            configuration["ProtoTest:Messaging:Broker"],
            "container",
            StringComparison.OrdinalIgnoreCase);
        var ownedDatabasePath = !usesPostgres && configuredDatabase is null
            ? Path.GetFullPath(Path.Combine("TestResults", "ProtoTest.Demo", "northstar-demo.db"))
            : null;

        return new DemoEnvironment(
            configuration,
            targetUrl,
            configuredDatabase,
            ownsPostgres,
            usesPostgres,
            configuredMessaging,
            ownsMessagingBroker,
            ownedDatabasePath);
    }

    /// <summary>
    /// Whether the configured store is PostgreSQL. An explicit provider wins; otherwise the
    /// connection string decides, and anything that is not clearly PostgreSQL stays SQLite.
    /// </summary>
    internal static bool IsPostgresStore(string? provider, string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(provider))
        {
            return string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase)
                || string.Equals(provider, "postgresql", StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        return connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);
    }

    public void PrepareOwnedDatabase()
    {
        if (OwnedDatabasePath is null) return;

        Directory.CreateDirectory(Path.GetDirectoryName(OwnedDatabasePath)!);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = OwnedDatabasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
