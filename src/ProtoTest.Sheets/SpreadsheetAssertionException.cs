namespace ProtoTest.Sheets;

using ProtoTest.Core;

/// <summary>A spreadsheet assertion failed; the message names the sheet, reference and actual value.</summary>
public sealed class SpreadsheetAssertionException : ProtoAssertionException
{
    public SpreadsheetAssertionException(string message)
        : base(message)
    {
    }
}
