using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class RefParamTests
{
    [Fact]
    public void Void_Method_With_Single_Ref_Returns_Bare_Value()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public static class K {
                public static void Increment(ref int x) { x = x + 1; }
                public static int Use(int x) {
                    Increment(ref x);
                    return x;
                }
            }
            """);
        Assert.Contains("export function Increment(x: number): number {", ts);
        Assert.Contains("return x;", ts);
        // Caller destructures into a plain assignment for single-ref.
        Assert.Contains("x = Increment(x);", ts);
    }

    [Fact]
    public void Void_Method_With_Multiple_Refs_Returns_Tuple()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public static class K {
                public static void Swap(ref int a, ref int b) {
                    int t = a; a = b; b = t;
                }
                public static int Use(int x, int y) {
                    Swap(ref x, ref y);
                    return x;
                }
            }
            """);
        Assert.Contains("export function Swap(a: number, b: number): [number, number] {", ts);
        Assert.Contains("return [a, b];", ts);
        Assert.Contains("[x, y] = Swap(x, y);", ts);
    }

    [Fact]
    public void Out_Param_Treated_Same_As_Ref()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public static class K {
                public static void Pair(int seed, out int a, out int b) {
                    a = seed;
                    b = seed + 1;
                }
                public static int Use(int s) {
                    int a = 0;
                    int b = 0;
                    Pair(s, out a, out b);
                    return a + b;
                }
            }
            """);
        Assert.Contains("export function Pair(seed: number, a: number, b: number): [number, number]", ts);
        Assert.Contains("[a, b] = Pair(s, a, b);", ts);
    }

    [Fact]
    public void Hilbert_Style_Rotate_Helper_Emits()
    {
        // Mirror of HilbertCurve.Rotate — the canonical v0.4 #64 shape.
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public static class H {
                public static int Apply(int n, int x, int y) {
                    Rotate(n, ref x, ref y, 1, 0);
                    return x + y;
                }
                private static void Rotate(int n, ref int x, ref int y, int rx, int ry) {
                    if (ry == 0) {
                        if (rx == 1) {
                            x = n - 1 - x;
                            y = n - 1 - y;
                        }
                        int t = x; x = y; y = t;
                    }
                }
            }
            """);
        Assert.Contains("export function Rotate(n: number, x: number, y: number, rx: number, ry: number): [number, number]", ts);
        Assert.Contains("return [x, y];", ts);
        Assert.Contains("[x, y] = Rotate(n, x, y, 1, 0);", ts);
    }
}
