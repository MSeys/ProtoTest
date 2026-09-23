namespace ProtoTest.Core;

using System.Diagnostics;

/// <summary>
/// The facts both trace writers record for a captured activity: its source, its tags under the shared
/// length cap, and its failure as a trace error.
/// </summary>
internal static class ProtoTraceActivity
{
    /// <summary>The attribute map an activity entry carries: its source plus every tag the cap keeps.</summary>
    public static Dictionary<string, string?> Attributes(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["activity.source"] = activity.Source.Name
        };
        foreach (var tag in activity.TagObjects)
        {
            // Application tags follow the same cap as the OpenTelemetry export: an oversized value
            // would otherwise grow the archive without ever leaving it as an exported attribute.
            var value = tag.Value?.ToString();
            if (value is { Length: > ProtoTestTraceRecorder.MaxTagValueLength }) continue;
            attributes[tag.Key] = value;
        }

        return attributes;
    }

    public static bool IsFailure(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return activity.Status == ActivityStatusCode.Error;
    }

    /// <summary>The activity's failure description, when it failed and described why.</summary>
    public static ProtoTraceError? Error(Activity activity)
        => IsFailure(activity) && activity.StatusDescription is { Length: > 0 } description
            ? new ProtoTraceError("ActivityError", description)
            : null;
}
