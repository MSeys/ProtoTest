namespace ProtoTest.Core.Internal;

/// <summary>
/// The facts a run records about the environment it executed in - the CI build and run it came from -
/// captured once from <see cref="ProtoTraceOptions"/>. The trace session merges them into the run's
/// <c>environment.*</c> attributes on the wire and the report sinks receive them as
/// <see cref="ProtoReportItemKinds.RunMetadata"/> items, so both evidence outputs read the same facts.
/// </summary>
internal sealed class ProtoRunMetadata : IProtoReportSource
{
    /// <summary>The built-in environment facts a metadata key would hide; the wire has one key per fact.</summary>
    private static readonly string[] BuiltInKeys = ["runtime", "os", "processArchitecture", "osArchitecture"];

    private ProtoRunMetadata(IReadOnlyDictionary<string, string> values) => Values = values;

    /// <summary>The metadata in configuration order; empty when the suite configured none.</summary>
    public IReadOnlyDictionary<string, string> Values { get; }

    public static ProtoRunMetadata Capture(ProtoTraceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        // Lifted variables come first, so an explicit value with the same name wins.
        foreach (var name in options.RunMetadataEnvironmentVariables)
        {
            Validate(name, nameof(options.RunMetadataEnvironmentVariables), "Name an environment variable such as 'GITHUB_RUN_ID'.");
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                values[name] = value;
            }
        }

        foreach (var (key, value) in options.RunMetadata)
        {
            Validate(key, nameof(options.RunMetadata), "Use a key such as 'ci.run_id'.");
            if (value is null)
            {
                throw new ArgumentException(
                    $"ProtoTraceOptions.RunMetadata['{key}'] must not be null. Use an empty string to record " +
                    "an empty value.",
                    nameof(options.RunMetadata));
            }

            values[key] = value;
        }

        return new ProtoRunMetadata(values);
    }

    private static void Validate(string key, string option, string fix)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException(
                $"ProtoTraceOptions.{option} must not contain an empty name. {fix}",
                option);
        }

        if (BuiltInKeys.Contains(key, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"ProtoTraceOptions.{option} carries '{key}', which is a built-in environment fact a " +
                $"metadata entry would hide. {fix}",
                option);
        }
    }

    public IEnumerable<ProtoReportItem> GetReportItems()
        => [.. Values.Select(pair => new ProtoReportItem(
            TargetName: "Run",
            Category: "Metadata",
            Identifier: pair.Key,
            Kind: ProtoReportItemKinds.RunMetadata,
            Status: ProtoReportStatus.Info,
            Message: pair.Value))];
}
