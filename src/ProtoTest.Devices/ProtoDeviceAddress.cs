namespace ProtoTest.Devices;

using ProtoTest.Core;

/// <summary>Builds the address resolvers device clients are declared with: a template, or an application's address plus a path.</summary>
public static class ProtoDeviceAddress
{
    /// <summary>Resolves <c>{deviceId}</c> in <paramref name="address"/> and appends an optional path template.</summary>
    public static Func<ProtoExecutionContext, string, string> Template(
        string address,
        string? path = null,
        Func<string, string>? transform = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return (_, deviceId) => Combine(transform is null ? address : transform(address), path, deviceId);
    }

    /// <summary>
    /// Resolves against the application's address for the running test (an infrastructure-published
    /// address wins over configuration), with an optional path template; the same suite follows the
    /// application into every environment.
    /// </summary>
    public static Func<ProtoExecutionContext, string, string> FromApplication(
        string applicationName,
        string? path = null,
        Func<string, string>? transform = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        return (context, deviceId) =>
        {
            var baseUrl = ProtoApplication.BaseUrl(context, applicationName) ?? throw new InvalidOperationException(
                $"Application '{applicationName}' has no address for this run. Configure 'ProtoTest:Applications:{applicationName}:BaseUrl'.");
            return Combine(transform is null ? baseUrl : transform(baseUrl), path, deviceId);
        };
    }

    private static string Combine(string baseAddress, string? path, string deviceId)
    {
        var escaped = Uri.EscapeDataString(deviceId);
        var address = baseAddress.Replace("{deviceId}", escaped, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(path))
        {
            return address;
        }

        var resolvedPath = path.Replace("{deviceId}", escaped, StringComparison.Ordinal);
        return Uri.TryCreate(address, UriKind.Absolute, out var origin)
            ? new Uri(origin, resolvedPath).ToString()
            : address + resolvedPath;
    }
}
