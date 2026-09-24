namespace ProtoTest.Sheets.Internal;

using DocumentFormat.OpenXml.Spreadsheet;

/// <summary>
/// Whether a cell's number format is a date, resolved once per workbook from its stylesheet: built-in
/// date and time ids, plus custom formats whose code carries a y, d or h token after literals are
/// removed. The code walk is assembly-visible so the parsing rules are tested directly.
/// </summary>
internal sealed class SheetDateFormat
{
    private readonly CellFormat[] _formats;
    private readonly Dictionary<uint, string?> _customFormats;

    private SheetDateFormat(CellFormat[] formats, Dictionary<uint, string?> customFormats)
    {
        _formats = formats;
        _customFormats = customFormats;
    }

    /// <summary>Resolves the date formats of a stylesheet; a workbook without one has no date cells.</summary>
    public static SheetDateFormat FromStylesheet(Stylesheet? stylesheet)
    {
        if (stylesheet?.CellFormats is null)
        {
            return new SheetDateFormat([], []);
        }

        var customFormats = stylesheet.NumberingFormats?.Elements<NumberingFormat>()
            .Where(format => format.NumberFormatId is not null && format.FormatCode is not null)
            .ToDictionary(format => format.NumberFormatId!.Value, format => format.FormatCode!.Value)
            ?? [];
        return new SheetDateFormat(stylesheet.CellFormats.Elements<CellFormat>().ToArray(), customFormats);
    }

    /// <summary>Whether the cell at this style index holds a date or time.</summary>
    public bool IsDateStyle(uint? styleIndex)
    {
        if (styleIndex is null || styleIndex.Value >= _formats.Length)
        {
            return false;
        }

        var formatId = _formats[(int)styleIndex.Value].NumberFormatId?.Value ?? 0;
        // Built-in date and time ids: 14-22 (dates/times), 27-36 (East Asian dates),
        // 45-47 (times) and 50-58 (East Asian dates/times).
        if (formatId is >= 14 and <= 22 or >= 27 and <= 36 or >= 45 and <= 47 or >= 50 and <= 58)
        {
            return true;
        }

        return _customFormats.TryGetValue(formatId, out var code) && !string.IsNullOrEmpty(code) && IsDateCode(code);
    }

    /// <summary>
    /// A format code is a date format when it carries a y, d or h token after literals are removed.
    /// Quoted text, bracketed sections (colors, conditions, locale ids) and escaped characters are
    /// literals, so a currency suffix like <c>#,##0.00 "USD"</c> must not read as a date. A bracketed
    /// elapsed-hours token like <c>[h]</c> is a time value, not a literal.
    /// </summary>
    public static bool IsDateCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var remaining = StripLiterals(code);
        return remaining.Contains('y', StringComparison.OrdinalIgnoreCase)
            || remaining.Contains('d', StringComparison.OrdinalIgnoreCase)
            || remaining.Contains('h', StringComparison.OrdinalIgnoreCase);
    }

    private static string StripLiterals(string code)
    {
        var tokens = new System.Text.StringBuilder(code.Length);
        for (var index = 0; index < code.Length; index++)
        {
            var current = code[index];
            if (current == '"')
            {
                // Quoted literals end at the next quote; "" inside one displays a single quote.
                for (index++; index < code.Length; index++)
                {
                    if (code[index] != '"')
                    {
                        continue;
                    }

                    if (index + 1 < code.Length && code[index + 1] == '"')
                    {
                        index++;
                        continue;
                    }

                    break;
                }

                continue;
            }

            if (current == '[')
            {
                var start = index + 1;
                while (index < code.Length && code[index] != ']')
                {
                    index++;
                }

                var content = code[start..Math.Min(index, code.Length)];
                if (content.Length is > 0 and <= 2 && content.All(token => token is 'h' or 'H'))
                {
                    // An elapsed-hours token ([h] or [hh]) is a time value, not a bracketed literal.
                    tokens.Append('h');
                }

                continue;
            }

            if (current is '\\' or '_' or '*')
            {
                index++;
                continue;
            }

            tokens.Append(current);
        }

        return tokens.ToString();
    }
}
