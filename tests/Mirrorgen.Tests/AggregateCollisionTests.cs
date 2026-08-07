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
    public void Colliding_Static_Singletons_Fail_Instead_Of_Silently_Dropping_One()
    {
        // Each class hoists `static readonly T Instance` to module scope, so
        // both land on the identifier `Instance`. This used to emit only
        // EquirectangularProjection's — OrthographicProjection.Instance simply
        // vanished and consumers had to call `new OrthographicProjection()`.
        var ex = Assert.Throws<NotSupportedException>(() => Aggregate(
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
                """)));

        Assert.Contains("'Instance'", ex.Message);
        // The message has to name both sides — knowing only that "something
        // called Instance collided" doesn't tell you where to put EmitName.
        Assert.Contains("EquirectangularProjection", ex.Message);
        Assert.Contains("OrthographicProjection", ex.Message);
        Assert.Contains("EmitName", ex.Message);
    }

    [Fact]
    public void EmitName_Resolves_The_Collision()
    {
        var all = Aggregate(
            ("A.cs", """
                using Mirrorgen;
                namespace X;
                [Transpile]
                public sealed class EquirectangularProjection {
                    [Transpile(EmitName = "EquirectangularInstance")]
                    public static readonly EquirectangularProjection Instance = new();
                    public int Project(int x) => x * 2;
                }
                """),
            ("B.cs", """
                using Mirrorgen;
                namespace X;
                [Transpile]
                public sealed class OrthographicProjection {
                    [Transpile(EmitName = "OrthographicInstance")]
                    public static readonly OrthographicProjection Instance = new();
                    public int Project(int x) => x * 3;
                }
                """));

        Assert.Contains("EquirectangularInstance", all);
        Assert.Contains("OrthographicInstance", all);
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
