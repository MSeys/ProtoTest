namespace ProtoTest.Json;

using System.Collections;
using System.Globalization;
using System.Text.Json;
using ProtoTest.Core;

/// <summary>
/// The context of one traced shape assertion: where it records, under which parent, with which extra
/// attributes, and whether the expected shape is captured as an attachment.
/// </summary>
public sealed record ProtoShapeAssertionContext(
    ProtoExecutionContext? Execution,
    string Source,
    string Title,
    string? ParentOperationId = null,
    IReadOnlyDictionary<string, string?>? ExtraAttributes = null,
    bool CaptureExpectedShape = false,
    string? AttachmentName = null,
    string? AttachmentDescription = null);

/// <summary>
/// The one traced shape assertion REST, GraphQL, gRPC and messaging share. It records the
/// <c>assert.json.shape</c> operation with the expected and actual JSON, the matched properties or every
/// mismatch, captures the expected shape when the protocol asks for it, and fails the operation before
/// rethrowing, so the evidence is in the trace even when the test fails.
/// </summary>
public static class ProtoShapeAssertion
{
    /// <summary>
    /// Matches <paramref name="actualJson"/> against <paramref name="expectedShape"/> and records the
    /// assertion on <see cref="ProtoShapeAssertionContext.Execution"/>. A null execution still matches
    /// and throws, untraced. A null <paramref name="expectedShape"/> expects JSON null, matching the
    /// matcher's documented null expectations. On success <paramref name="observation"/> builds the
    /// protocol's observation from the matched properties; returning null records none. A mismatch
    /// fails the operation and rethrows the shape exception. Returns the matched property paths.
    /// </summary>
    public static IReadOnlyList<string> Assert(
        ProtoShapeAssertionContext context,
        string? actualJson,
        object? expectedShape,
        JsonSerializerOptions? options = null,
        JsonDiagnosticOptions? diagnosticOptions = null,
        Func<IReadOnlyList<string>, ProtoObservation?>? observation = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var operation = context.Execution is null
            ? null
            : context.Execution.Trace
                .Operation("assert.json.shape", context.Title, context.Source)
                .With("expected.type", expectedShape?.GetType().FullName)
                .With(context.ExtraAttributes)
                .Parent(context.ParentOperationId)
                .Begin();

        try
        {
            // Describing the expected shape can itself fail (a hostile or cyclic shape); it must run
            // inside the traced operation so the failure is recorded rather than escaping untraced.
            var expectedShapeJson = JsonDiagnosticSanitizer.Serialize(DescribeExpectedValue(expectedShape), diagnosticOptions);
            var actualShapeJson = actualJson is null
                ? null
                : JsonDiagnosticSanitizer.Sanitize(actualJson, diagnosticOptions);
            operation?.SetAttribute("shape.expected", expectedShapeJson);
            operation?.SetAttribute("shape.actual", actualShapeJson);

            if (context.CaptureExpectedShape && context.Execution is not null && context.AttachmentName is { Length: > 0 } attachmentName)
            {
                context.Execution.AddAttachment(
                    attachmentName,
                    expectedShapeJson,
                    "application/json",
                    context.AttachmentDescription);
            }

            var matchedProperties = JsonShapeMatcher.AssertMatch(actualJson ?? string.Empty, expectedShape, options);
            operation?.SetAttribute("matched.property_count", matchedProperties.Count.ToString());
            operation?.SetAttribute("matched.properties", string.Join(", ", matchedProperties));
            operation?.SetAttribute("shape.matches", JsonDiagnosticSanitizer.Serialize(matchedProperties, diagnosticOptions));
            operation?.SetAttribute("shape.result", "matched");
            operation?.AddSection(new ProtoTraceSection(
                "Result",
                ProtoTraceSectionKind.Checks,
                [
                    new(
                        "shape",
                        $"{matchedProperties.Count} {(matchedProperties.Count == 1 ? "property" : "properties")}",
                        Tone: ProtoTraceSectionTone.Success)
                ]));
            if (context.Execution is not null && observation?.Invoke(matchedProperties) is { } recorded)
            {
                context.Execution.RecordObservation(recorded);
            }

            operation?.Succeed();
            return matchedProperties;
        }
        catch (JsonShapeMismatchException exception)
        {
            operation?.SetAttribute("shape.result", "mismatched");
            operation?.SetAttribute("shape.matches", JsonDiagnosticSanitizer.Serialize(exception.MatchedProperties, diagnosticOptions));
            operation?.SetAttribute("shape.mismatches", JsonDiagnosticSanitizer.Serialize(exception.Mismatches, diagnosticOptions));
            operation?.SetAttribute("shape.mismatch_count", exception.Mismatches.Count.ToString());
            operation?.AddSection(new ProtoTraceSection(
                "Result",
                ProtoTraceSectionKind.Checks,
                [
                    new(
                        "shape",
                        $"{exception.Mismatches.Count} {(exception.Mismatches.Count == 1 ? "mismatch" : "mismatches")}",
                        exception.Message,
                        ProtoTraceSectionTone.Error)
                ]));
            operation?.Fail(exception);
            throw;
        }
        catch (Exception exception)
        {
            operation?.Fail(exception);
            throw;
        }
    }

    /// <summary>The maximum nesting depth described before a placeholder is recorded.</summary>
    private const int MaxDescriptionDepth = 16;

    /// <summary>
    /// Expands a shape into a serializable value: value constraints become their description, and nested
    /// objects and arrays are expanded so the trace records the shape the test actually declared.
    /// Dictionaries are described as objects and the depth is capped, so cyclic or pathologically deep
    /// shapes cannot exhaust the stack while the description is built.
    /// </summary>
    private static object? DescribeExpectedValue(object? expected, int depth = 0)
    {
        if (expected is null)
        {
            return null;
        }

        if (expected is IJsonValueMatcher matcher)
        {
            return $"constraint: {matcher.Description}";
        }

        if (depth >= MaxDescriptionDepth)
        {
            return $"<{expected.GetType().Name} at depth limit>";
        }

        var type = expected.GetType();
        if (type.IsPrimitive || type.IsEnum || expected is string or decimal or DateTime or DateTimeOffset or Guid)
        {
            return expected;
        }

        // A dictionary is an object shape, not a sequence of its key/value pairs. Keys are stringified,
        // matching the matcher: JSON object names are always strings.
        if (expected is IDictionary dictionary)
        {
            var described = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
            {
                described[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = DescribeExpectedValue(entry.Value, depth + 1);
            }

            return described;
        }

        if (expected is IEnumerable values)
        {
            return values.Cast<object?>().Select(item => DescribeExpectedValue(item, depth + 1)).ToArray();
        }

        return type.GetProperties()
            .Where(property => property.GetIndexParameters().Length == 0)
            .ToDictionary(
                property => property.Name,
                property => DescribeExpectedValue(property.GetValue(expected), depth + 1));
    }
}
