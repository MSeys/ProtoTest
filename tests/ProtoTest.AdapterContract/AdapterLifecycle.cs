namespace ProtoTest.AdapterContract;

using ProtoTest.Core;

/// <summary>
/// The shared adapter contract: the lifecycle sequence every adapter must preserve, the check a
/// compliance test body runs, and the host configuration the adapter test projects share. One copy
/// keeps the cross-adapter comparison meaningful and stops the copies from drifting.
/// </summary>
public static class AdapterLifecycle
{
    /// <summary>The lifecycle events every adapter must record before the test body runs.</summary>
    public static readonly string[] ExpectedBeforeSequence =
    [
        "Hook:Before",
        "Class:Before",
        "Method:Before"
    ];

    internal static readonly string[] ExpectedCompletedSequence =
    [
        .. ExpectedBeforeSequence,
        "Test",
        "Method:After",
        "Class:After",
        "Hook:After"
    ];

    /// <summary>Registers the tracking hook every adapter test project shares.</summary>
    public static void ConfigureHost(IProtoHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddTestHook<TrackingHook>();
    }

    /// <summary>
    /// Verifies the before lifecycle, the test's identity and the attachment surface, then marks the
    /// test as contract-verified so the tracking hook validates the completed sequence at teardown.
    /// </summary>
    public static void VerifyTestBody<TDeclaringType>(string methodName)
    {
        var context = Proto.Context;
        var state = context.Resolve<ExecutionLogState>();

        Ensure(
            state.Log.SequenceEqual(ExpectedBeforeSequence),
            $"Unexpected before lifecycle: {string.Join(", ", state.Log)}");
        Ensure(context.TestMethod.DeclaringType == typeof(TDeclaringType),
            $"Expected declaring type '{typeof(TDeclaringType).FullName}', but found '{context.TestMethod.DeclaringType?.FullName}'.");
        Ensure(context.TestMethod.Name == methodName,
            $"Expected method '{methodName}', but found '{context.TestMethod.Name}'.");
        Ensure(!string.IsNullOrWhiteSpace(context.TestName), "The adapter supplied an empty test name.");
        Ensure(context.TestId.Length is > 0 and <= 18 && context.TestId.All(char.IsAsciiDigit),
            $"The adapter supplied invalid test ID '{context.TestId}'.");
        Ensure(context.TestNumber == long.Parse(context.TestId),
            "The numeric and string test ID representations differ.");

        context.AddAttachment(
            "adapter-contract",
            "ProtoTest adapter attachment publishing succeeded.",
            description: "Shared adapter contract artifact");

        state.Log.Add("Test");
        state.ContractVerified = true;
    }

    internal static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"ProtoTest adapter contract failed. {message}");
        }
    }
}
