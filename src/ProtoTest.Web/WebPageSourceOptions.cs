namespace ProtoTest.Web;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Where the page inventory comes from: the frontend source folder and the framework to scan it as.
/// Its settings live under <c>ProtoTest:Web:Pages</c>, whose other keys hold the explicit inventory
/// entries, so it reads its two keys directly instead of binding the whole section.
/// </summary>
public sealed record WebPageSourceOptions(
    string? SourceFolder = null,
    WebPageFramework Framework = WebPageFramework.Auto)
{
    /// <summary>The configuration section the source settings live under.</summary>
    public const string ConfigurationSectionName = "ProtoTest:Web:Pages";

    /// <summary>Reads the source settings; an unknown framework value means <see cref="WebPageFramework.Auto"/>.</summary>
    public static WebPageSourceOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(ConfigurationSectionName);
        return new WebPageSourceOptions(
            section["Source"],
            Enum.TryParse<WebPageFramework>(section["Framework"], ignoreCase: true, out var framework)
                ? framework
                : WebPageFramework.Auto);
    }
}
