namespace ProtoTest.Analyzers;

using System.Linq;
using Microsoft.CodeAnalysis;

/// <summary>Decides whether a test method runs inside the ProtoTest lifecycle.</summary>
internal static class ProtoTestMethods
{
    /// <summary>
    /// True for a method with a ProtoTest attribute, a plain test the assembly's auto-wrap covers, or a
    /// TUnit test the ProtoTest executor runs.
    /// </summary>
    public static bool RunsInLifecycle(IMethodSymbol method)
    {
        if (method.Find(ProtoTestVocabulary.LifecycleAttributes) is not null)
        {
            return true;
        }

        if (IsAutoWrapped(method))
        {
            return true;
        }

        return method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == ProtoTestVocabulary.TUnitTestAttribute)
            && UsesProtoTestExecutor(method);
    }

    /// <summary>True when the method's plain test attribute is one its assembly's auto-wrap covers.</summary>
    public static bool IsAutoWrapped(IMethodSymbol method)
    {
        foreach (var assemblyAttribute in method.ContainingAssembly.GetAttributes())
        {
            if (assemblyAttribute.AttributeClass is { } attributeClass &&
                ProtoTestVocabulary.AutoWrapAttributes.TryGetValue(attributeClass.ToDisplayString(), out var wrapped) &&
                method.Find(wrapped) is not null)
            {
                return true;
            }
        }

        return false;
    }

    // TestExecutor<ProtoTestExecutor> may sit on the method, any containing type or the assembly.
    private static bool UsesProtoTestExecutor(IMethodSymbol method)
    {
        if (NamesExecutor(method))
        {
            return true;
        }

        for (var type = method.ContainingType; type is not null; type = type.ContainingType)
        {
            if (NamesExecutor(type))
            {
                return true;
            }
        }

        return method.ContainingAssembly.GetAttributes().Any(NamesExecutor);
    }

    private static bool NamesExecutor(ISymbol symbol) => symbol.GetAttributes().Any(NamesExecutor);

    private static bool NamesExecutor(AttributeData attribute)
        => attribute.AttributeClass?.TypeArguments.Any(argument => argument.ToDisplayString() == ProtoTestVocabulary.TUnitExecutor) == true;

    /// <summary>
    /// Resolves the method an operation belongs to, so a local function reports through the test that
    /// declares it. Lambdas have no symbol of their own and already report as their method.
    /// </summary>
    public static IMethodSymbol? EnclosingMethod(ISymbol symbol)
    {
        while (symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction } localFunction)
        {
            symbol = localFunction.ContainingSymbol;
        }

        return symbol as IMethodSymbol;
    }
}
