namespace ProtoTest.Sheets.Internal;

/// <summary>Parses and formats A1-style spreadsheet references.</summary>
internal static class SheetReferences
{
    public static (int Column, int Row) Parse(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (!TryParse(reference, out var column, out var row))
        {
            throw new FormatException($"'{reference}' is not an A1-style cell reference.");
        }

        return (column, row);
    }

    /// <summary>Tries to parse an A1-style reference; file data may be malformed and must not throw.</summary>
    public static bool TryParse(string? reference, out int column, out int row)
    {
        column = 0;
        row = 0;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var index = 0;
        while (index < reference.Length && char.IsLetter(reference[index]))
        {
            column = (column * 26) + (char.ToUpperInvariant(reference[index]) - 'A' + 1);
            index++;
        }

        while (index < reference.Length && char.IsDigit(reference[index]))
        {
            row = (row * 10) + (reference[index] - '0');
            index++;
        }

        // The workbook format's own limits; they also keep a corrupt reference from overflowing.
        return column != 0 && row != 0 && index == reference.Length
            && column <= 16_384 && row <= 1_048_576;
    }

    public static string Format(int column, int row)
    {
        var letters = string.Empty;
        while (column > 0)
        {
            var remainder = (column - 1) % 26;
            letters = (char)('A' + remainder) + letters;
            column = (column - 1) / 26;
        }

        return $"{letters}{row}";
    }
}
