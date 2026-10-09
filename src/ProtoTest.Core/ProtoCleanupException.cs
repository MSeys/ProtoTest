namespace ProtoTest.Core;

using System.Runtime.ExceptionServices;

/// <summary>
/// The failure a runner reports when cleanup failed and <see cref="ProtoCleanupFailureMode.Fail"/> is
/// in effect. A body that passed produces a message that says so and names the first cleanup failure;
/// further failures are counted. A body that failed stays the primary error: its message leads, it is
/// the <see cref="Exception.InnerException"/>, and <see cref="CleanupFailures"/> carries the cleanup.
/// </summary>
public sealed class ProtoCleanupException : Exception
{
    private ProtoCleanupException(
        string message,
        Exception? inner,
        IReadOnlyList<Exception> cleanupFailures,
        bool bodyPassed)
        : base(message, inner)
    {
        CleanupFailures = cleanupFailures;
        BodyPassed = bodyPassed;
    }

    /// <summary>The cleanup failures, in the order teardown observed them.</summary>
    public IReadOnlyList<Exception> CleanupFailures { get; }

    /// <summary>Whether the test body itself passed and this failure exists only because cleanup failed.</summary>
    public bool BodyPassed { get; }

    /// <summary>
    /// A failure for a body that passed. The message names the first cleanup failure and counts the rest.
    /// </summary>
    public static ProtoCleanupException ForPassedBody(Exception cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        return ForPassedBody(Flatten(cleanup));
    }

    /// <summary>
    /// A failure for a body that passed. The message names the first cleanup failure and counts the rest.
    /// </summary>
    public static ProtoCleanupException ForPassedBody(IReadOnlyList<Exception> cleanupFailures)
    {
        ArgumentNullException.ThrowIfNull(cleanupFailures);
        var failures = Copy(cleanupFailures.SelectMany(Flatten).ToArray());
        if (failures.Length == 0)
        {
            throw new ArgumentException("At least one cleanup failure is required.", nameof(cleanupFailures));
        }
        return Create(
            $"The test body passed, but cleanup failed: {Describe(failures)}",
            failures[0],
            failures,
            bodyPassed: true);
    }

    /// <summary>
    /// A failure whose message and inner exception are the body's own failure, with the cleanup failures
    /// attached after it.
    /// </summary>
    public static ProtoCleanupException ForBodyFailure(ProtoTestResult result, Exception cleanup)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(cleanup);
        var failures = Copy(Flatten(cleanup));
        var body = result.Exception;
        var bodyMessage = body?.Message ?? result.Error?.Message ?? "The test body failed.";
        return Create(
            bodyMessage + Environment.NewLine + Environment.NewLine + "Cleanup also failed: " + Describe(failures),
            body,
            failures,
            bodyPassed: false);
    }

    private static ProtoCleanupException Create(
        string message,
        Exception? primary,
        IReadOnlyList<Exception> cleanupFailures,
        bool bodyPassed)
    {
        var exception = new ProtoCleanupException(message, primary, cleanupFailures, bodyPassed);
        // The runner displays this exception, so its stack should be the failure the message names:
        // the body's, when the body failed, otherwise the first cleanup failure.
        if (!string.IsNullOrEmpty(primary?.StackTrace))
        {
            ExceptionDispatchInfo.SetRemoteStackTrace(exception, primary.StackTrace);
        }

        return exception;
    }

    private static string Describe(IReadOnlyList<Exception> failures)
    {
        var first = failures[0];
        var message = $"{first.GetType().Name}: {first.Message}.";
        var rest = failures.Count - 1;
        if (rest > 0)
        {
            message += $" {rest} more cleanup {(rest == 1 ? "failure" : "failures")}.";
        }

        return message;
    }

    private static List<Exception> Flatten(Exception exception)
        => exception is AggregateException aggregate
            ? [.. aggregate.Flatten().InnerExceptions]
            : [exception];

    private static Exception[] Copy(IReadOnlyList<Exception> failures) => [.. failures];
}
