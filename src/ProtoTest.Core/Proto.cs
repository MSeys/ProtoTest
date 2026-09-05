namespace ProtoTest.Core;

/// <summary>
/// Provides a static gateway to the active <see cref="ProtoExecutionContext"/>.
/// </summary>
public static class Proto
{
    /// <summary>
    /// Gets the current active test execution context.
    /// </summary>
    public static ProtoExecutionContext Context => ProtoHost.Current;
}