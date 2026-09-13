namespace ProtoTest.Data;

/// <summary>Explains how ProtoTest.Data resolved an object's values.</summary>
public sealed record ProtoDataExplanation(
    Type TargetType,
    IReadOnlyList<ProtoDataValueExplanation> Values,
    string ConstructionSource);

/// <summary>Explains the resolved value and origin for one constructor parameter or property.</summary>
public sealed record ProtoDataValueExplanation(
    string MemberName,
    Type ValueType,
    object? Value,
    string SourceKind,
    string Source);
