using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class CrossFileConstTests : IDisposable
{
    readonly string _root;

    public CrossFileConstTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mirrorgen-xconst-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Cross_File_Const_In_Const_Initialiser_Resolves_To_Literal()
    {
        // FixedPoint.Scale lives in one file; Encoding consumes it.
        // The emitted Encoding.ts should carry the resolved literal (256/2 = 128).
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "FixedPoint.cs"), """
            [Mirrorgen.Transpile]
            public static class FixedPoint {
                public const int FractionBits = 8;
                public const int Scale = 1 << FractionBits;
            }
            """);
        File.WriteAllText(Path.Combine(src, "Encoding.cs"), """
            [Mirrorgen.Transpile]
            public static class Encoding {
                public const int WaterVisibilityThresholdFp = FixedPoint.Scale / 2;
                public const int WaterMaxDepthFp = 8 * FixedPoint.Scale;
            }
            """);
        var outDir = Path.Combine(_root, "out");
        BatchTranspiler.TranspileDirectory(src, outDir);

        var encoding = File.ReadAllText(Path.Combine(outDir, "Encoding.ts"));
        Assert.Contains("export const WaterVisibilityThresholdFp: number = 128;", encoding);
        Assert.Contains("export const WaterMaxDepthFp: number = 2048;", encoding);
    }

    [Fact]
    public void Cross_File_Const_Reference_In_Method_Body_Inlines_Literal()
    {
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "K1.cs"), """
            [Mirrorgen.Transpile]
            public static class K1 {
                public const int Base = 100;
            }
            """);
        File.WriteAllText(Path.Combine(src, "K2.cs"), """
            public static class K2 {
                [Mirrorgen.Transpile]
                public static int F(int x) => K1.Base + x;
            }
            """);
        var outDir = Path.Combine(_root, "out");
        BatchTranspiler.TranspileDirectory(src, outDir);

        var k2 = File.ReadAllText(Path.Combine(outDir, "K2.ts"));
        // Const should be inlined (literal 100); int-arithmetic wrap is still applied.
        // What must NOT appear is the unresolved `K1.Base` qualified form, which would
        // be a runtime ReferenceError in TS (K1 has no class binding emitted).
        Assert.Contains("100 + x", k2);
        Assert.DoesNotContain("K1.Base", k2);
    }

    [Fact]
    public void Same_File_Const_Reference_Still_Works()
    {
        // Regression: single-file const reference inside a [Transpile] body must
        // continue to emit either the literal or the bare identifier (no broken
        // member access).
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public static class K {
                public const int Base = 100;
                public static int F(int x) => Base + x;
            }
            """);
        Assert.Contains("export const Base: number = 100;", ts);
        // Either `100 + x` (literal inline) or `Base + x` (identifier) is acceptable.
        Assert.True(ts.Contains("100 + x") || ts.Contains("Base + x"),
            "F's body should reference Base either by literal or by identifier; got: " + ts);
    }
}
