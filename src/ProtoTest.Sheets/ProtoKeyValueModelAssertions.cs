namespace ProtoTest.Sheets;

/// <summary>
/// The assertions of one key-value model, reached through
/// <see cref="ProtoKeyValueModel{TModel}.Should"/>; each member returns the model, so assertions chain.
/// </summary>
public sealed class ProtoKeyValueModelAssertions<TModel> where TModel : notnull
{
    private readonly ProtoKeyValueModel<TModel> _model;
    private readonly bool _negated;

    internal ProtoKeyValueModelAssertions(ProtoKeyValueModel<TModel> model, bool negated)
    {
        _model = model;
        _negated = negated;
    }

    /// <summary>
    /// Checks every declared label against the sheet: the label must exist exactly once, the value must
    /// convert to the property's type, a non-nullable property must not be empty, and
    /// <c>Min</c>/<c>Max</c>/<c>Pattern</c>/<c>OneOf</c> must hold. All violations are reported in one
    /// failure. Returns the model.
    /// </summary>
    public ProtoKeyValueModel<TModel> MatchModel() => _model.AssertModel(_negated);
}
