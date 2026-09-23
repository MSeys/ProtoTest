namespace ProtoTest.Sql;

/// <summary>
/// The hook orders <c>ProtoTest.Sql</c> composes its setup from. Setup hooks run in ascending order, so
/// the connection hook runs first and the Entity Framework Core enlistment hook after it; the numbers
/// live here rather than in each hook so the coupling is stated once.
/// </summary>
public static class SqlHookOrders
{
    /// <summary>The connection hook, which opens the connection and starts the test's transaction.</summary>
    public const int Connection = -1_000;

    /// <summary>
    /// The Entity Framework Core enlistment hook, which needs the open connection and its transaction.
    /// </summary>
    public const int Enlistment = -999;
}
