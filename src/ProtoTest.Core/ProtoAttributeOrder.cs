namespace ProtoTest.Core;

/// <summary>
/// Named order bands for setup attributes, so an attribute sits relative to the built-ins without
/// spelling out their raw values. Lower orders run earlier; two attributes with the same order run in
/// the order the runner reports them, so a suite sequences its own attributes with
/// <see cref="ProtoAttribute.Order"/>.
/// </summary>
public static class ProtoAttributeOrder
{
    /// <summary>
    /// The application selection: a web session's declaration and a login both read it, so it runs
    /// before every other attribute.
    /// </summary>
    public const int Application = int.MinValue;

    /// <summary>A web session is declared before a login runs, so the login finds an open session.</summary>
    public const int SessionDeclaration = -10;

    /// <summary>Where attributes with no explicit order run; logins use it.</summary>
    public const int Default = 0;
}
