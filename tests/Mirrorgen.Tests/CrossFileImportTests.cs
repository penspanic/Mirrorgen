using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

/// <summary>
/// Types reachable from a sibling tree are inlined into each emit, but methods
/// are not — a per-file emit that calls one needs an import. Without it the
/// build stayed green and the module died at run time on an undefined
/// identifier.
/// </summary>
public class CrossFileImportTests : IDisposable
{
    readonly string _root;

    public CrossFileImportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mirrorgen-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    string Emit(TranspileOptions options, params (string file, string source)[] files)
    {
        var src = Path.Combine(_root, "src");
        foreach (var (file, source) in files)
        {
            var full = Path.Combine(src, file);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
        }
        var outDir = Path.Combine(_root, "out");
        BatchTranspiler.TranspileDirectory(src, outDir, options);
        return outDir;
    }

    static (string helpers, string uses) TwoFiles => ("""
        using Mirrorgen;
        namespace X;
        [Transpile] public enum Kind { A = 0, B = 1 }
        [Transpile] public readonly record struct Point(int X, int Y);
        public static class Helpers {
            [Transpile] public static int Clamp(int v) => v < 0 ? 0 : v;
            [Transpile] public static int Twice(int v) => v * 2;
        }
        """, """
        using Mirrorgen;
        namespace X;
        public static class Uses {
            [Transpile] public static Point Make(int x, Kind k) => new Point(Helpers.Twice(Helpers.Clamp(x)), (int)k);
        }
        """);

    [Fact]
    public void Cross_File_Call_Emits_An_Import()
    {
        var (helpers, uses) = TwoFiles;
        var outDir = Emit(TranspileOptions.Default, ("A.cs", helpers), ("B.cs", uses));
        var b = File.ReadAllText(Path.Combine(outDir, "B.ts"));

        // One statement for both names, sorted, `.js` by default.
        Assert.Contains("import { Clamp, Twice } from './A.js';", b);
        Assert.Contains("export function Make(", b);
    }

    [Fact]
    public void Import_Extension_Is_Configurable()
    {
        var (helpers, uses) = TwoFiles;

        var tsDir = Emit(new TranspileOptions { ImportExtension = ".ts" }, ("A.cs", helpers), ("B.cs", uses));
        Assert.Contains("from './A.ts';", File.ReadAllText(Path.Combine(tsDir, "B.ts")));

        Directory.Delete(Path.Combine(_root, "out"), recursive: true);
        var bareDir = Emit(new TranspileOptions { ImportExtension = "" }, ("A.cs", helpers), ("B.cs", uses));
        Assert.Contains("from './A';", File.ReadAllText(Path.Combine(bareDir, "B.ts")));
    }

    [Fact]
    public void Specifier_Is_Relative_Across_Subdirectories()
    {
        var (helpers, uses) = TwoFiles;
        var outDir = Emit(TranspileOptions.Default,
            ("core/A.cs", helpers),
            ("app/nested/B.cs", uses));
        var b = File.ReadAllText(Path.Combine(outDir, "app", "nested", "B.ts"));
        Assert.Contains("from '../../core/A.js';", b);
    }

    [Fact]
    public void Types_Are_Still_Inlined_Rather_Than_Imported()
    {
        // Types are duplicated into every tree that reaches them, so importing
        // them would be a second declaration of the same shape.
        var (helpers, uses) = TwoFiles;
        var outDir = Emit(TranspileOptions.Default, ("A.cs", helpers), ("B.cs", uses));
        var b = File.ReadAllText(Path.Combine(outDir, "B.ts"));

        Assert.Contains("export interface Point {", b);
        Assert.Contains("export enum Kind {", b);
        Assert.DoesNotContain("Point,", b.Split('\n')[0]);
        Assert.DoesNotContain("Kind", b.Split('\n')[0]);
    }

    [Fact]
    public void Aggregate_Mode_Emits_No_Imports()
    {
        // One module — the file boundary the import would cross is gone.
        var (helpers, uses) = TwoFiles;
        var outDir = Emit(new TranspileOptions { AggregateOutputFile = "all.ts" }, ("A.cs", helpers), ("B.cs", uses));
        var all = File.ReadAllText(Path.Combine(outDir, "all.ts"));

        Assert.DoesNotContain("import ", all);
        Assert.Contains("export function Clamp(", all);
        Assert.Contains("export function Make(", all);
    }

    [Fact]
    public void Same_File_Calls_Do_Not_Import()
    {
        var outDir = Emit(TranspileOptions.Default, ("Solo.cs", """
            using Mirrorgen;
            namespace X;
            public static class Solo {
                [Transpile] public static int Inner(int v) => v + 1;
                [Transpile] public static int Outer(int v) => Inner(v);
            }
            """));
        var ts = File.ReadAllText(Path.Combine(outDir, "Solo.ts"));
        Assert.DoesNotContain("import ", ts);
    }

    [Fact]
    public void Renamed_Method_Is_Imported_Under_Its_Emit_Name()
    {
        var outDir = Emit(TranspileOptions.Default,
            ("A.cs", """
                using Mirrorgen;
                namespace X;
                public static class Helpers {
                    [Transpile(EmitName = "clampValue")] public static int Clamp(int v) => v < 0 ? 0 : v;
                }
                """),
            ("B.cs", """
                using Mirrorgen;
                namespace X;
                public static class Uses {
                    [Transpile] public static int Go(int v) => Helpers.Clamp(v);
                }
                """));
        var b = File.ReadAllText(Path.Combine(outDir, "B.ts"));
        Assert.Contains("import { clampValue } from './A.js';", b);
        Assert.Contains("return clampValue(v);", b);
    }
}
