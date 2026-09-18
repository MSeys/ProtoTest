namespace ProtoTest.Core.Internal;

/// <summary>Projects a resource snapshot into the report item every sink can render.</summary>
internal static class ProtoResourceReportItemExtensions
{
    public static ProtoReportItem ToReportItem(
        this ProtoResourceSnapshot resource,
        string targetName,
        string scope,
        string? displayGroup)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["resource.kind"] = resource.Kind,
            ["resource.scope"] = scope,
            ["resource.state"] = resource.State.ToString()
        };

        if (resource.ReleaseDuration is { } duration)
        {
            metadata["resource.release_ms"] = Math.Round(duration.TotalMilliseconds, 3);
        }

        if (!string.IsNullOrWhiteSpace(resource.Error))
        {
            metadata["resource.error"] = resource.Error;
        }

        return new ProtoReportItem(
            TargetName: targetName,
            Category: "Resources",
            Identifier: resource.Id,
            Kind: ProtoReportItemKinds.Resource,
            Status: resource.State switch
            {
                ProtoResourceState.Released => ProtoReportStatus.Success,
                ProtoResourceState.ReleaseFailed => ProtoReportStatus.Error,
                _ => ProtoReportStatus.Neutral
            },
            Message: resource.Description,
            Metadata: metadata,
            DisplayName: $"{resource.Kind} · {resource.Id}",
            DisplayGroup: displayGroup);
    }
}
