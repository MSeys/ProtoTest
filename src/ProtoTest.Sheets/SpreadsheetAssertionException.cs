namespace ProtoTest.Sheets;

using ProtoTest.Core;

/// <summary>A spreadsheet assertion failed; the message names the sheet, reference and actual value.</summary>
public sealed class SpreadsheetAssertionException : ProtoAssertionException
{
    public SpreadsheetAssertionException(string message)
        : base(message)
    {
    }

    /// <summary>Wraps a shared shape assertion failure, keeping its mismatch details inspectable.</summary>
    public SpreadsheetAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
