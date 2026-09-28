namespace ProtoTest.Feedback;

using System.Globalization;
using System.Text;

/// <summary>
/// The documented GitHub Actions workflow command. The runner reads one command per line, so data and
/// property values are escaped as the runner requires and a message never spans lines.
/// </summary>
public static class ProtoWorkflowCommand
{
    /// <summary>
    /// Renders one <c>::error</c> annotation. The file and line properties are omitted when absent;
    /// the runner shows the message in the pull request's changed files when a file is given.
    /// </summary>
    public static string Error(string message, string? file = null, int? line = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var command = new StringBuilder("::error");
        if (!string.IsNullOrWhiteSpace(file))
        {
            command.Append(" file=").Append(EscapeProperty(file));
            if (line is { } number and > 0)
            {
                command.Append(",line=").Append(number.ToString(CultureInfo.InvariantCulture));
            }
        }

        return command.Append("::").Append(EscapeData(message)).ToString();
    }

    private static string EscapeData(string value)
        => value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);

    private static string EscapeProperty(string value)
        => EscapeData(value)
            .Replace(":", "%3A", StringComparison.Ordinal)
            .Replace(",", "%2C", StringComparison.Ordinal);
}
