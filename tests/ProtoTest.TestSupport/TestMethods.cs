namespace ProtoTest.TestSupport;

using System.Reflection;

/// <summary>
/// The method identity a test passes to <c>StartTestAsync</c> when only the display name matters.
/// One placeholder replaces the per-suite private <c>TestMethod()</c> reflection helpers.
/// </summary>
public static class TestMethods
{
    /// <summary>A real method of this type, used as the test's method identity.</summary>
    public static readonly MethodInfo Placeholder =
        typeof(TestMethods).GetMethod(nameof(PlaceholderMethod), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void PlaceholderMethod()
    {
    }
}
