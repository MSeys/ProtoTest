namespace ProtoTest.Sheets;

/// <summary>
/// The assertions of one record model, reached through <see cref="ProtoSheetModel{TRow}.Should"/>; each
/// member returns the model, so assertions chain.
/// </summary>
public sealed class ProtoSheetModelAssertions<TRow> where TRow : notnull
{
    private readonly ProtoSheetModel<TRow> _model;

    internal ProtoSheetModelAssertions(ProtoSheetModel<TRow> model)
    {
        _model = model;
    }

    /// <summary>
    /// Checks every declared column against the record's shape and reports all violations in one
    /// failure. Returns the model.
    /// </summary>
    public ProtoSheetModel<TRow> MatchModel() => _model.AssertModel();
}
