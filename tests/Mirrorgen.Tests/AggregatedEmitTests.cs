using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class AggregatedEmitTests : IDisposable
{
    readonly string _root;

    public AggregatedEmitTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mirrorgen-aggregate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Aggregate_Mode_Writes_Single_File()
    {
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "Foo.cs"), """
            [Mirrorgen.Transpile]
            public record Foo(int X);
            """);
        File.WriteAllText(Path.Combine(src, "Bar.cs"), """
            [Mirrorgen.Transpile]
            public record Bar(string Name);
            """);

        var outDir = Path.Combine(_root, "out");
        var opts = new TranspileOptions { AggregateOutputFile = "all.ts" };
        var result = BatchTranspiler.TranspileDirectory(src, outDir, opts);

        // Aggregate mode writes exactly one file, not per-tree files.
        Assert.False(File.Exists(Path.Combine(outDir, "Foo.ts")));
        Assert.False(File.Exists(Path.Combine(outDir, "Bar.ts")));
        Assert.True(File.Exists(Path.Combine(outDir, "all.ts")));

        var all = File.ReadAllText(Path.Combine(outDir, "all.ts"));
        Assert.Contains("export interface Foo {", all);
        Assert.Contains("export interface Bar {", all);
        Assert.Contains("X: number;", all);
        Assert.Contains("Name: string;", all);
    }

    [Fact]
    public void Aggregate_Mode_Dedupes_Same_Interface_Across_Trees()
    {
        // Two consumer methods in different trees reference the same record.
        // Multi-file reachability inlines the record into each consumer's
        // .ts; the aggregated emit must collapse the duplicates into one
        // interface declaration.
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "Shape.cs"), """
            [Mirrorgen.Transpile]
            public record Shape(int Sides);
            """);
        File.WriteAllText(Path.Combine(src, "A.cs"), """
            public static class A {
                [Mirrorgen.Transpile]
                public static int Sides(Shape s) => s.Sides;
            }
            """);
        File.WriteAllText(Path.Combine(src, "B.cs"), """
            public static class B {
                [Mirrorgen.Transpile]
                public static int Half(Shape s) => s.Sides / 2;
            }
            """);

        var outDir = Path.Combine(_root, "out");
        var opts = new TranspileOptions { AggregateOutputFile = "all.ts" };
        BatchTranspiler.TranspileDirectory(src, outDir, opts);

        var all = File.ReadAllText(Path.Combine(outDir, "all.ts"));
        var firstIdx = all.IndexOf("export interface Shape {");
        var lastIdx = all.LastIndexOf("export interface Shape {");
        Assert.True(firstIdx >= 0);
        Assert.Equal(firstIdx, lastIdx);  // exactly one occurrence
        Assert.Contains("export function Sides(", all);
        Assert.Contains("export function Half(", all);
    }

    [Fact]
    public void Aggregate_Mode_Dedupes_Helper_Functions()
    {
        // Math.Round triggers __mirrorgen_bankersRound. Two consumer files
        // would each get their own copy in file-per-tree mode; aggregate
        // mode must keep exactly one.
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "A.cs"), """
            public static class A {
                [Mirrorgen.Transpile]
                public static double R(double x) => System.Math.Round(x);
            }
            """);
        File.WriteAllText(Path.Combine(src, "B.cs"), """
            public static class B {
                [Mirrorgen.Transpile]
                public static double R2(double x) => System.Math.Round(x);
            }
            """);

        var outDir = Path.Combine(_root, "out");
        var opts = new TranspileOptions { AggregateOutputFile = "all.ts" };
        BatchTranspiler.TranspileDirectory(src, outDir, opts);

        var all = File.ReadAllText(Path.Combine(outDir, "all.ts"));
        var firstIdx = all.IndexOf("function __mirrorgen_bankersRound");
        var lastIdx = all.LastIndexOf("function __mirrorgen_bankersRound");
        Assert.True(firstIdx >= 0);
        Assert.Equal(firstIdx, lastIdx);  // exactly one helper definition
    }

    [Fact]
    public void Default_Mode_Without_Aggregate_Still_Writes_Per_Tree()
    {
        // Regression check — leaving AggregateOutputFile null keeps the
        // existing one-file-per-source-tree behaviour.
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "Foo.cs"), """
            [Mirrorgen.Transpile]
            public record Foo(int X);
            """);
        File.WriteAllText(Path.Combine(src, "Bar.cs"), """
            [Mirrorgen.Transpile]
            public record Bar(string Y);
            """);

        var outDir = Path.Combine(_root, "out");
        BatchTranspiler.TranspileDirectory(src, outDir);

        Assert.True(File.Exists(Path.Combine(outDir, "Foo.ts")));
        Assert.True(File.Exists(Path.Combine(outDir, "Bar.ts")));
    }
}
