namespace ProtoTest.Sheets;

using ProtoTest.Sheets.Internal;

/// <summary>
/// The assertions of one record model, reached through <see cref="ProtoSheetModel{TRow}.Should"/> and
/// <see cref="ProtoSheetModel{TRow}.ShouldNot"/>; each member returns the model, so assertions chain.
/// </summary>
public sealed class ProtoSheetModelAssertions<TRow> where TRow : notnull
{
    private readonly ProtoSheetModel<TRow> _model;
    private readonly bool _negated;

    internal ProtoSheetModelAssertions(ProtoSheetModel<TRow> model, bool negated)
    {
        _model = model;
        _negated = negated;
    }

    private string Subject => $"the headers of '{_model.Sheet.Name}'";

    private static string Expectation => $"match {typeof(TRow).Name}'s declared columns in order";

    /// <summary>
    /// Checks the sheet's header row against the model's declared columns: every <c>[Column]</c> path
    /// must appear at its declaration position, the sheet must declare exactly as many columns as the
    /// model, and a single-segment path matches the end of a layered path. The header read contributes
    /// coverage. Returns the model.
    /// </summary>
    public ProtoSheetModel<TRow> MatchHeaders()
    {
        _model.RecordHeaderRead();
        var declared = _model.DeclaredHeaders;
        var actual = _model.SheetHeaders;
        var difference = SheetHeaderShape.Difference(declared, actual);
        SheetAssertion.Run(
            _model.Context,
            $"Sheets · {_model.Sheet.Name} headers",
            new Dictionary<string, string?>
            {
                ["sheets.sheet"] = _model.Sheet.Name,
                ["sheets.expected"] = SheetHeaderShape.Describe(declared),
                ["sheets.actual"] = SheetHeaderShape.Describe(actual)
            },
            _negated,
            () => difference is null,
            () => new SheetAssertionFailure(
                _negated
                    ? $"{SheetAssertion.Describe(Subject, Expectation, negated: true)} but they did."
                    : $"{SheetAssertion.Describe(Subject, Expectation, negated: false)} but {difference}."));
        return _model;
    }

    /// <summary>
    /// Checks every declared column against the record's shape and reports all violations in one
    /// failure. Returns the model.
    /// </summary>
    public ProtoSheetModel<TRow> MatchModel() => _model.AssertModel(_negated);
}
