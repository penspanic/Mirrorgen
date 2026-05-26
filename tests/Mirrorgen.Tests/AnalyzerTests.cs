using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Mirrorgen.Analyzers;
using Xunit;

namespace Mirrorgen.Tests;

public class AnalyzerTests
{
    [Fact]
    public async Task MG0001_Flags_Linq_In_Transpile_Method()
    {
        var source = """
            using System.Linq;

            public static class S
            {
                [Mirrorgen.Transpile]
                public static int F(int[] xs) => xs.Sum();
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        var mg = Assert.Single(diags, d => d.Id == SubsetAnalyzer.MG0001Id);
        Assert.Equal(DiagnosticSeverity.Error, mg.Severity);
        Assert.Contains("LINQ", mg.GetMessage());
    }

    [Fact]
    public async Task MG0001_Reports_Multiple_Linq_Calls()
    {
        var source = """
            using System.Linq;

            public static class S
            {
                [Mirrorgen.Transpile]
                public static int F(int[] xs) => xs.Where(x => x > 0).Select(x => x * 2).Sum();
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.Equal(3, diags.Count(d => d.Id == SubsetAnalyzer.MG0001Id));
    }

    [Fact]
    public async Task MG0001_Ignores_Linq_Outside_Transpile_Method()
    {
        var source = """
            using System.Linq;

            public static class S
            {
                public static int F(int[] xs) => xs.Where(x => x > 0).Count();
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == SubsetAnalyzer.MG0001Id);
    }

    [Fact]
    public async Task MG0001_Allows_Non_Linq_Invocations_In_Transpile_Method()
    {
        var source = """
            public static class S
            {
                [Mirrorgen.Transpile]
                public static int Inc(int x) => Add(x, 1);

                [Mirrorgen.Transpile]
                public static int Add(int a, int b) => a + b;
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == SubsetAnalyzer.MG0001Id);
    }

    static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnostics(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var refs = BuildReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTestInput",
            syntaxTrees: new[] { tree },
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new SubsetAnalyzer();
        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    static MetadataReference[] BuildReferences()
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        var refs = tpa
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        refs.Add(MetadataReference.CreateFromFile(typeof(Mirrorgen.TranspileAttribute).Assembly.Location));
        return refs.ToArray();
    }
}
