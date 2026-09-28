namespace ProtoTest.Web;

/// <summary>
/// The naming rule web artifacts share: lowercased identifiers made of letters, digits and single
/// dashes, so a session or element name can never break a file name. Backends use it for their failure
/// artifacts and native diagnostics; the failure helpers apply the same rule internally.
/// </summary>
public static class WebArtifactNames
{
    /// <summary>Reduces a value to lowercase letters, digits, and single dashes for use in file names.</summary>
    public static string SafeName(string value) => WebNames.SafeName(value);
}
