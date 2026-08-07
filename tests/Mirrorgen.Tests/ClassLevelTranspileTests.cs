using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class ClassLevelTranspileTests
{
    [Fact]
    public void Class_Level_Transpile_Emits_All_Public_Static_Methods()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public static class K {
                public const byte First = 2;
                public static byte Plus(byte a, byte b) => (byte)(a + b);
                public static int Triple(int x) => x * 3;
            }
            """);
        Assert.Contains("export const First: number = 2;", ts);
        Assert.Contains("export function Plus(", ts);
        Assert.Contains("export function Triple(", ts);
    }

    [Fact]
    public void Class_Level_Transpile_Skips_Non_Public_Static_Methods()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public static class K {
                public static int Pub(int x) => x;
                internal static int Internal(int x) => x;
                private static int Priv(int x) => x;
            }
            """);
        Assert.Contains("export function Pub(", ts);
        Assert.DoesNotContain("Internal(", ts);
        Assert.DoesNotContain("Priv(", ts);
    }

    [Fact]
    public void Method_Level_Transpile_Still_Works_Without_Class_Level()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class K {
                [Mirrorgen.Transpile]
                public static int F(int x) => x + 1;
                public static int G(int x) => x * 2;
            }
            """);
        Assert.Contains("export function F(", ts);
        Assert.DoesNotContain("export function G(", ts);
    }

    [Fact]
    public void Both_Class_Level_And_Method_Level_Idempotent()
    {
        // class-level + per-method [Transpile] should emit each method exactly once.
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public static class K {
                [Mirrorgen.Transpile] public static int F(int x) => x + 1;
            }
            """);
        var firstIndex = ts.IndexOf("export function F(");
        var lastIndex = ts.LastIndexOf("export function F(");
        Assert.True(firstIndex >= 0, "F should be emitted");
        Assert.Equal(firstIndex, lastIndex);
    }
}
