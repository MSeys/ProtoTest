namespace ProtoTest.Analyzers;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

/// <summary>Attribute lookups shared by the analyzers.</summary>
internal static class AttributeMatching
{
    /// <summary>Returns the first attribute on the method whose metadata name is listed, or null.</summary>
    public static AttributeData? Find(this IMethodSymbol method, ImmutableHashSet<string> attributeNames)
        => method.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass is { } attributeClass && attributeNames.Contains(attributeClass.ToDisplayString()));

    /// <summary>The attribute's own location when the source is present, otherwise the symbol's.</summary>
    public static Location LocationOf(AttributeData attribute, ISymbol symbol)
        => attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
            ?? symbol.Locations.FirstOrDefault()
            ?? Location.None;
}
