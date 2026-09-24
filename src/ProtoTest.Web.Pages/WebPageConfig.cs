namespace ProtoTest.Web.Pages;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Reads a page-list section the one way every inventory producer reads it: values from scalar, array
/// or object children, with configuration keys the section reserves for itself skipped.
/// </summary>
public static class WebPageConfig
{
    public static IReadOnlyList<string> Read(
        IConfiguration configuration,
        string sectionPath,
        params string[] reservedKeys)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var reserved = new HashSet<string>(reservedKeys, StringComparer.OrdinalIgnoreCase);
        var section = configuration.GetSection(sectionPath);
        var values = section.GetChildren()
            .Where(child => !reserved.Contains(child.Key))
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
        if (values.Count == 0 && !string.IsNullOrWhiteSpace(section.Value))
        {
            values.Add(section.Value!);
        }

        return values;
    }
}
