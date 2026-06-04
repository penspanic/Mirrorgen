using Mirrorgen.Core;
using Xunit;
using Xunit.Abstractions;

namespace Mirrorgen.Tests;

// W4 — the correctness gate: every representative WGSL emit must compile
// under naga, not merely contain the right substrings.
public class WgslNagaValidationTests
{
    readonly ITestOutputHelper _out;
    public WgslNagaValidationTests(ITestOutputHelper output) => _out = output;

    void AssertValid(string csharp)
    {
        var wgsl = TranspilerEngine.TranspileSourceToWgsl(csharp);
        if (!WgslNaga.Available)
        {
            _out.WriteLine("naga not found — skipping WGSL compile validation. Install with `cargo install naga-cli`.");
            return;
        }
        var (ok, output) = WgslNaga.Validate(wgsl);
        Assert.True(ok, $"naga rejected generated WGSL:\n{output}\n--- source ---\n{wgsl}");
    }

    [Fact]
    public void Scalar_Function_Compiles()
    {
        AssertValid("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static double Smoothstep(double edge0, double edge1, double x) {
                    if (edge1 <= edge0) { return x >= edge1 ? 1d : 0d; }
                    double t = (x - edge0) / (edge1 - edge0);
                    if (t < 0d) t = 0d;
                    else if (t > 1d) t = 1d;
                    return t * t * (3d - 2d * t);
                }
            }
            """);
    }

    [Fact]
    public void Tuple_Struct_And_Byte_Math_Compiles()
    {
        AssertValid("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static (byte R, byte G, byte B) MixRgb(
                    (byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double t) {
                    if (t <= 0d) return a;
                    if (t >= 1d) return b;
                    byte r = (byte)(a.R + (b.R - a.R) * t);
                    byte g = (byte)(a.G + (b.G - a.G) * t);
                    byte bl = (byte)(a.B + (b.B - a.B) * t);
                    return (r, g, bl);
                }
            }
            """);
    }

    [Fact]
    public void Buffer_Bindings_And_Calls_Compile()
    {
        AssertValid("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static double Smoothstep(double edge0, double edge1, double x) {
                    if (edge1 <= edge0) { return x >= edge1 ? 1d : 0d; }
                    double t = (x - edge0) / (edge1 - edge0);
                    if (t < 0d) t = 0d;
                    else if (t > 1d) t = 1d;
                    return t * t * (3d - 2d * t);
                }

                [Mirrorgen.Attributes.Transpile]
                public static (byte R, byte G, byte B) MixRgb(
                    (byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double t) {
                    if (t <= 0d) return a;
                    if (t >= 1d) return b;
                    byte r = (byte)(a.R + (b.R - a.R) * t);
                    byte g = (byte)(a.G + (b.G - a.G) * t);
                    byte bl = (byte)(a.B + (b.B - a.B) * t);
                    return (r, g, bl);
                }

                [Mirrorgen.Attributes.Transpile]
                public static (byte R, byte G, byte B) SampleBedrock(
                    byte heightByte,
                    double absLat,
                    [Mirrorgen.WgslBuffer(Group = 0, Binding = 2)] double[] bandThresholds,
                    [Mirrorgen.WgslBuffer(Group = 0, Binding = 3)] (byte R, byte G, byte B)[] bandColors,
                    double polarLatMin,
                    double polarLatMax,
                    (byte R, byte G, byte B) polarColor)
                {
                    if (bandColors.Length == 0) { return polarColor; }
                    double h = heightByte / 255d;
                    var c = bandColors[0];
                    for (int i = 1; i < bandColors.Length; i++) {
                        double t = Smoothstep(bandThresholds[i - 1], bandThresholds[i], h);
                        c = MixRgb(c, bandColors[i], t);
                    }
                    double polarT = Smoothstep(polarLatMin, polarLatMax, absLat);
                    return MixRgb(c, polarColor, polarT);
                }
            }
            """);
    }
}
