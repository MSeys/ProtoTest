namespace ProtoTest.Web;

/// <summary>Typed table foundation built on the same lazy component collection as lists and grids.</summary>
public abstract class WebTable<TRow> : WebComponent where TRow : WebComponent, new()
{
    /// <summary>Override for legacy tables or grids whose rows need CSS or another semantic locator.</summary>
    protected virtual WebLocator RowLocator => By.Role(WebRole.Row);

    public WebComponentCollection<TRow> Rows => Components<TRow>(RowLocator, nameof(Rows));

    /// <summary>Returns a lazy row at a zero-based index.</summary>
    public TRow RowAt(int index, string? name = null) => Rows.At(index, name);

    /// <summary>Returns a lazy row by one-based number.</summary>
    public TRow RowNumber(int number, string? name = null) => Rows.Number(number, name);

    /// <summary>Returns a strict lazy row selected by a semantic filter such as By.HasText().</summary>
    public TRow RowMatching(WebLocator condition, string? name = null) => Rows.Matching(condition, name);
}

/// <summary>Base row with zero-based and one-based cell addressing for conventional and legacy tables.</summary>
public abstract class WebTableRow : WebComponent
{
    public WebElement CellAt(int index, string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return Element(By.TableCellAt(index), name ?? $"Cell[{index}]");
    }

    public WebElement CellNumber(int number, string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        return Element(By.TableCellNumber(number), name ?? $"Cell[{number}]");
    }

    public WebElement Cell(string header, string? name = null, bool exact = true, bool ignoreCase = false)
        => Element(By.TableCell(header, exact, ignoreCase), name ?? $"Cell[{header}]");
}
