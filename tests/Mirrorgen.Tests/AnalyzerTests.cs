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

    [Fact]
    public async Task MG0002_Flags_Async_Modifier()
    {
        var source = """
            using System.Threading.Tasks;

            public static class S
            {
                [Mirrorgen.Transpile]
                public static async Task<int> F() { await Task.Delay(1); return 1; }
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.Contains(diags, d => d.Id == SubsetAnalyzer.MG0002Id && d.GetMessage().Contains("async"));
    }

    [Fact]
    public async Task MG0002_Flags_Await_Expression()
    {
        var source = """
            using System.Threading.Tasks;

            public static class S
            {
                [Mirrorgen.Transpile]
                public static async Task F() => await Task.Delay(1);
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.Contains(diags, d => d.Id == SubsetAnalyzer.MG0002Id && d.GetMessage().Contains("await"));
    }

    [Fact]
    public async Task MG0002_Flags_Task_Return_Type_Without_Async()
    {
        var source = """
            using System.Threading.Tasks;

            public static class S
            {
                [Mirrorgen.Transpile]
                public static Task<int> F() => Task.FromResult(1);
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.Contains(diags, d => d.Id == SubsetAnalyzer.MG0002Id && d.GetMessage().Contains("Task"));
    }

    [Fact]
    public async Task MG0002_Flags_ValueTask_Return_Type()
    {
        var source = """
            using System.Threading.Tasks;

            public static class S
            {
                [Mirrorgen.Transpile]
                public static ValueTask<int> F() => new ValueTask<int>(1);
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.Contains(diags, d => d.Id == SubsetAnalyzer.MG0002Id);
    }

    [Fact]
    public async Task MG0002_Ignores_Async_Outside_Transpile_Method()
    {
        var source = """
            using System.Threading.Tasks;

            public static class S
            {
                public static async Task<int> F() { await Task.Delay(1); return 1; }
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == SubsetAnalyzer.MG0002Id);
    }

    [Fact]
    public async Task MG0003_Allows_Ref_And_Out_Parameters()
    {
        // `ref` / `out` / `in` parameters are local, emit as tuple
        // destructuring, and keep values as values across the boundary —
        // nothing about them fails to mirror. The walker has supported them
        // for a while (see RefParamTests); the analyzer used to disagree.
        var source = """
            public static class S
            {
                [Mirrorgen.Transpile]
                public static void F(ref int x) { x = x + 1; }

                [Mirrorgen.Transpile]
                public static void G(int seed, out int a, out int b) { a = seed; b = seed + 1; }

                [Mirrorgen.Transpile]
                public static int H(in int x) => x;
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == SubsetAnalyzer.MG0003Id);
    }

    [Fact]
    public async Task MG0003_Flags_Ref_Return()
    {
        // Unlike a ref parameter, a ref *return* is a genuine alias into the
        // caller's storage — it cannot be modelled as an extra return value.
        var source = """
            public static class S
            {
                static int _v;
                [Mirrorgen.Transpile]
                public static ref int F() => ref _v;
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.Contains(diags, d => d.Id == SubsetAnalyzer.MG0003Id);
    }

    [Fact]
    public async Task MG0003_Flags_Ref_Struct_Parameter()
    {
        var source = """
            public ref struct Window { public int Start; }

            public static class S
            {
                [Mirrorgen.Transpile]
                public static int F(Window w) => w.Start;
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.Contains(diags, d => d.Id == SubsetAnalyzer.MG0003Id);
    }

    [Fact]
    public async Task MG0003_Flags_Span_Parameter()
    {
        var source = """
            using System;

            public static class S
            {
                [Mirrorgen.Transpile]
                public static int F(Span<int> xs) => xs.Length;
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.Contains(diags, d => d.Id == SubsetAnalyzer.MG0003Id);
    }

    [Fact]
    public async Task MG0003_Ignores_Plain_Parameters()
    {
        var source = """
            public static class S
            {
                [Mirrorgen.Transpile]
                public static int F(int x, bool b) => x;
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == SubsetAnalyzer.MG0003Id);
    }

    [Fact]
    public async Task MG0004_Flags_Throw_Statement()
    {
        var source = """
            using System;

            public static class S
            {
                [Mirrorgen.Transpile]
                public static int F(int x) { if (x < 0) throw new ArgumentException(); return x; }
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.Contains(diags, d => d.Id == SubsetAnalyzer.MG0004Id);
    }

    [Fact]
    public async Task MG0004_Ignores_Throw_Outside_Transpile_Method()
    {
        var source = """
            using System;

            public static class S
            {
                public static int F() { throw new InvalidOperationException(); }
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == SubsetAnalyzer.MG0004Id);
    }

    [Fact]
    public async Task MG0005_Flags_Reflection_Invocation()
    {
        var source = """
            using System.Reflection;

            public static class S
            {
                [Mirrorgen.Transpile]
                public static string F() => typeof(S).GetMethod("F")!.Name;
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.Contains(diags, d => d.Id == SubsetAnalyzer.MG0005Id);
    }

    [Fact]
    public async Task MG0006_Flags_Inheriting_Declarer()
    {
        var source = """
            public class Base { }

            public class Derived : Base
            {
                [Mirrorgen.Transpile]
                public static int F() => 1;
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.Contains(diags, d => d.Id == SubsetAnalyzer.MG0006Id);
    }

    [Fact]
    public async Task MG0006_Ignores_Object_Base()
    {
        var source = """
            public static class S
            {
                [Mirrorgen.Transpile]
                public static int F() => 1;
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == SubsetAnalyzer.MG0006Id);
    }

    [Fact]
    public async Task MG0004_Allows_Throw_In_A_Switch_Expression_Discard_Arm()
    {
        // A throw here asserts the switch is total rather than describing
        // behaviour — reaching it is already a bug, so there is nothing for a
        // fixture to disagree about. Mirrorgen emits a throw in exactly this
        // position as its own no-arm-matched safety net.
        var source = """
            using System;

            public enum K { A = 0, B = 1 }

            public static class S
            {
                [Mirrorgen.Transpile]
                public static int Map(K k) => k switch
                {
                    K.A => 1,
                    K.B => 2,
                    _ => throw new ArgumentOutOfRangeException(nameof(k)),
                };
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == SubsetAnalyzer.MG0004Id);
    }

    [Fact]
    public async Task MG0004_Still_Flags_Throw_In_A_Non_Discard_Arm()
    {
        // Same syntax, reachable position: this one is control flow.
        var source = """
            using System;

            public enum K { A = 0, B = 1 }

            public static class S
            {
                [Mirrorgen.Transpile]
                public static int Map(K k) => k switch
                {
                    K.A => throw new ArgumentException("nope"),
                    _ => 2,
                };
            }
            """;
        var diags = await GetAnalyzerDiagnostics(source);
        Assert.Contains(diags, d => d.Id == SubsetAnalyzer.MG0004Id);
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
