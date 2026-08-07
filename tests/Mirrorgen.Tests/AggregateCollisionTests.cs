using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

/// <summary>
/// Aggregated emit folds N per-tree outputs into one module. Deduping by name
/// alone cannot tell "the same type inlined into two trees" (fine, drop the
/// extra) from "two different members that collided on one identifier" (not
/// fine — dropping one leaves the module quietly missing a declaration).
/// The text of the block is what separates the two cases.
/// </summary>
public class AggregateCollisionTests : IDisposable
{
    readonly string _root;

    public AggregateCollisionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mirrorgen-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    string Aggregate(params (string file, string source)[] files)
    {
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        foreach (var (file, source) in files)
        {
            File.WriteAllText(Path.Combine(src, file), source);
        }
        var outDir = Path.Combine(_root, "out");
        BatchTranspiler.TranspileDirectory(src, outDir, new TranspileOptions { AggregateOutputFile = "all.ts" });
        return File.ReadAllText(Path.Combine(outDir, "all.ts"));
    }

    [Fact]
    public void Class_Shape_Singletons_No_Longer_Collide()
    {
        // These used to be two module-scope `const Instance`, so only the first
        // survived. Statics on a class-shape type are members of their class
        // now, which namespaces them without anyone having to intervene.
        var all = Aggregate(
            ("A.cs", """
                using Mirrorgen;
                namespace X;
                [Transpile]
                public sealed class EquirectangularProjection {
                    public static readonly EquirectangularProjection Instance = new();
                    public int Project(int x) => x * 2;
                }
                """),
            ("B.cs", """
                using Mirrorgen;
                namespace X;
                [Transpile]
                public sealed class OrthographicProjection {
                    public static readonly OrthographicProjection Instance = new();
                    public int Project(int x) => x * 3;
                }
                """));

        Assert.Contains("static Instance: EquirectangularProjection = new EquirectangularProjection();", all);
        Assert.Contains("static Instance: OrthographicProjection = new OrthographicProjection();", all);
        Assert.DoesNotContain("export const Instance", all);
    }

    [Fact]
    public void Colliding_Module_Scope_Statics_Fail_Instead_Of_Silently_Dropping_One()
    {
        // Interface-shape types (records / structs) have nowhere to put a
        // static, so theirs still hoist to module scope — and two of them can
        // still land on one identifier. Dropping the second silently is what
        // this guards against.
        var ex = Assert.Throws<NotSupportedException>(() => Aggregate(
            ("A.cs", """
                using Mirrorgen;
                namespace X;
                [Transpile] public readonly record struct CellId(int Value) {
                    public static readonly CellId Invalid = default;
                }
                """),
            ("B.cs", """
                using Mirrorgen;
                namespace X;
                [Transpile] public readonly record struct EdgeId(int Value) {
                    public static readonly EdgeId Invalid = default;
                }
                """)));

        Assert.Contains("'Invalid'", ex.Message);
        Assert.Contains("CellId", ex.Message);
        Assert.Contains("EdgeId", ex.Message);
        Assert.Contains("EmitName", ex.Message);
    }

    [Fact]
    public void EmitName_Resolves_The_Collision()
    {
        var all = Aggregate(
            ("A.cs", """
                using Mirrorgen;
                namespace X;
                [Transpile] public readonly record struct CellId(int Value) {
                    [Transpile(EmitName = "CellIdInvalid")]
                    public static readonly CellId Invalid = default;
                }
                """),
            ("B.cs", """
                using Mirrorgen;
                namespace X;
                [Transpile] public readonly record struct EdgeId(int Value) {
                    [Transpile(EmitName = "EdgeIdInvalid")]
                    public static readonly EdgeId Invalid = default;
                }
                """));

        Assert.Contains("CellIdInvalid", all);
        Assert.Contains("EdgeIdInvalid", all);
    }

    [Fact]
    public void Same_Type_Inlined_Into_Two_Trees_Still_Dedupes()
    {
        // The legitimate duplicate: `Point` is reachable from both files, so
        // both per-tree emits carry an identical `export interface Point`.
        // Identical text — collapse to one, no error.
        var all = Aggregate(
            ("Shared.cs", """
                using Mirrorgen;
                namespace X;
                [Transpile] public readonly record struct Point(int X, int Y);
                """),
            ("A.cs", """
                using Mirrorgen;
                namespace X;
                public static class MakesA { [Transpile] public static Point A(int v) => new Point(v, 1); }
                """),
            ("B.cs", """
                using Mirrorgen;
                namespace X;
                public static class MakesB { [Transpile] public static Point B(int v) => new Point(v, 2); }
                """));

        var occurrences = all.Split("export interface Point").Length - 1;
        Assert.Equal(1, occurrences);
        Assert.Contains("export function A(", all);
        Assert.Contains("export function B(", all);
    }

    [Fact]
    public void Colliding_Functions_Fail_Too()
    {
        // Not specific to static fields — any two top-level declarations that
        // land on the same identifier are a silent-drop hazard.
        var ex = Assert.Throws<NotSupportedException>(() => Aggregate(
            ("A.cs", """
                using Mirrorgen;
                namespace X;
                public static class First { [Transpile] public static int Scale(int v) => v * 2; }
                """),
            ("B.cs", """
                using Mirrorgen;
                namespace Y;
                public static class Second { [Transpile] public static int Scale(int v) => v * 3; }
                """)));

        Assert.Contains("'Scale'", ex.Message);
    }
}
