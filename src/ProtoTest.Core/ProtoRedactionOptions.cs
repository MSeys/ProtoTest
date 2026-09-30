namespace ProtoTest.Core;

/// <summary>
/// Names a suite redacts on top of the defaults: every additional name is treated like the entries
/// of <see cref="ProtoRedactionDefaults.SensitivePropertyNames"/> wherever the run redacts state
/// values and finding metadata, while attachment and diagnostic JSON keeps each protocol's
/// <c>SensitiveJsonProperties</c> list. The section <c>ProtoTest:Redaction</c> binds over code values when
/// the host is built.
/// </summary>
public sealed class ProtoRedactionOptions : IProtoConfigurableOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string ConfigurationSectionName = "ProtoTest:Redaction";

    /// <inheritdoc />
    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>
    /// Property names redacted in addition to the defaults, matched case-insensitively. Code and
    /// configuration compose: callbacks add here and the section binds over them.
    /// </summary>
    public IList<string> AdditionalSensitiveNames { get; } = new List<string>();

    /// <summary>Adds one property name to redact, returning the options so calls chain.</summary>
    public ProtoRedactionOptions AddSensitiveName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        AdditionalSensitiveNames.Add(name);
        return this;
    }

    /// <inheritdoc />
    public void Validate()
    {
        foreach (var name in AdditionalSensitiveNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "Additional sensitive names must not be blank.", nameof(AdditionalSensitiveNames));
            }
        }
    }

    /// <summary>
    /// The configured names as the redaction consumers read them, or null when the suite added
    /// none and the defaults alone apply.
    /// </summary>
    internal IReadOnlyCollection<string>? SnapshotAdditionalNames()
        => AdditionalSensitiveNames.Count == 0 ? null : [.. AdditionalSensitiveNames];
}
