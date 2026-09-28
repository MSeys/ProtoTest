namespace ProtoTest.Analyzers.Tests;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Compiles a fixture source against the real runner and framework assemblies - so the attribute
/// names the analyzers match are the shipped ones - and returns the analyzer diagnostics.
/// </summary>
internal static class AnalyzerTestFixture
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References =
        new(BuildReferences, LazyThreadSafetyMode.ExecutionAndPublication);

    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        DiagnosticAnalyzer analyzer,
        string source)
    {
        var compilation = CreateCompilation(source);
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                "The fixture source does not compile: " + string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
        }

        return await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "AnalyzerFixture",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
        {
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
            {
                references.TryAdd(path, MetadataReference.CreateFromFile(path));
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Add(assembly);
        }

        // The markers guarantee the assemblies a fixture needs are loaded, whatever the test order is.
        Add(typeof(ProtoTest.Core.Proto).Assembly);
        Add(typeof(ProtoTest.Rest.ProtoExecutionContextExtensions).Assembly);
        Add(typeof(ProtoTest.Http.ProtoHttpResponse).Assembly);
        Add(typeof(ProtoTest.Sheets.SheetAttribute).Assembly);
        Add(typeof(ProtoTest.NUnit.ProtoTestAttribute).Assembly);
        Add(typeof(global::NUnit.Framework.TestAttribute).Assembly);
        Add(typeof(ProtoTest.Xunit3.ProtoTestFactAttribute).Assembly);
        Add(typeof(global::Xunit.FactAttribute).Assembly);
        Add(typeof(global::Xunit.TheoryAttribute).Assembly);

        return [.. references.Values.OrderBy(reference => reference.Display, StringComparer.Ordinal)];

        void Add(Assembly assembly)
        {
            if (!string.IsNullOrEmpty(assembly.Location))
            {
                references.TryAdd(assembly.Location, MetadataReference.CreateFromFile(assembly.Location));
            }
        }
    }
}
