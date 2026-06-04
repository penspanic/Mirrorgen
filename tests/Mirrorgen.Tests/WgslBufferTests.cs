using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

// W3 — array parameters become storage-buffer bindings ([WgslBuffer]),
// .Length → arrayLength, element access, calls to other transpiled fns,
// and `var` type inference (incl. inferred struct locals).
public class WgslBufferTests
{
    // The three-function surface mirrors TidemarkSurfaceColorModel's bedrock
    // path: Smoothstep + MixRgb (W2) called from SampleBedrock (W3).
    const string Source = """
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
        """;

    static string Wgsl() => TranspilerEngine.TranspileSourceToWgsl(Source);

    [Fact]
    public void Array_Params_Become_Storage_Bindings()
    {
        var wgsl = Wgsl();
        Assert.Contains("@group(0) @binding(2) var<storage, read> bandThresholds: array<f32>;", wgsl);
        Assert.Contains("@group(0) @binding(3) var<storage, read> bandColors: array<MgTuple_RGB>;", wgsl);
    }

    [Fact]
    public void Buffer_Params_Dropped_From_Signature()
    {
        var wgsl = Wgsl();
        Assert.Contains(
            "fn SampleBedrock(heightByte: u32, absLat: f32, polarLatMin: f32, polarLatMax: f32, polarColor: MgTuple_RGB) -> MgTuple_RGB {",
            wgsl);
    }

    [Fact]
    public void Length_Lowers_To_ArrayLength_As_I32()
    {
        var wgsl = Wgsl();
        Assert.Contains("i32(arrayLength(&bandColors)) == 0", wgsl);
        Assert.Contains("i < i32(arrayLength(&bandColors))", wgsl);
    }

    [Fact]
    public void Element_Access_Indexes_Binding()
    {
        var wgsl = Wgsl();
        Assert.Contains("bandThresholds[i - 1]", wgsl);
        Assert.Contains("bandColors[i]", wgsl);
    }

    [Fact]
    public void Var_Infers_Struct_Local()
    {
        var wgsl = Wgsl();
        Assert.Contains("var c: MgTuple_RGB = bandColors[0];", wgsl);
    }

    [Fact]
    public void Calls_Other_Transpiled_Functions()
    {
        var wgsl = Wgsl();
        Assert.Contains("Smoothstep(bandThresholds[i - 1], bandThresholds[i], h)", wgsl);
        Assert.Contains("c = MixRgb(c, bandColors[i], t);", wgsl);
        Assert.Contains("return MixRgb(c, polarColor, polarT);", wgsl);
    }

    [Fact]
    public void Byte_Div_Double_Lifts_To_F32()
    {
        var wgsl = Wgsl();
        Assert.Contains("let h: f32 = f32(heightByte) / 255.0;", wgsl);
    }
}
