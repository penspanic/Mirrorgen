using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

// W2 — composite types (named tuple → WGSL struct), C# implicit numeric
// promotion reproduced as explicit WGSL conversions (the byte-parity crux),
// casts, and for-loops.
public class WgslCompositeTests
{
    static string Wgsl(string members) =>
        TranspilerEngine.TranspileSourceToWgsl($$"""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                {{members}}
            }
            """);

    const string MixRgb = """
        public static (byte R, byte G, byte B) MixRgb(
            (byte R, byte G, byte B) a,
            (byte R, byte G, byte B) b,
            double t)
        {
            if (t <= 0d) return a;
            if (t >= 1d) return b;
            byte r = (byte)(a.R + (b.R - a.R) * t);
            byte g = (byte)(a.G + (b.G - a.G) * t);
            byte bl = (byte)(a.B + (b.B - a.B) * t);
            return (r, g, bl);
        }
        """;

    [Fact]
    public void Named_Tuple_Becomes_Struct()
    {
        var wgsl = Wgsl(MixRgb);
        Assert.Contains("struct MgTuple_RGB {", wgsl);
        Assert.Contains("  R: u32,", wgsl);
        Assert.Contains("  G: u32,", wgsl);
        Assert.Contains("  B: u32,", wgsl);
    }

    [Fact]
    public void Struct_Declared_Before_Function()
    {
        var wgsl = Wgsl(MixRgb);
        Assert.True(wgsl.IndexOf("struct MgTuple_RGB") < wgsl.IndexOf("fn MixRgb"),
            "struct must precede the function that uses it");
    }

    [Fact]
    public void Fn_Signature_Uses_Struct_Type()
    {
        var wgsl = Wgsl(MixRgb);
        Assert.Contains("fn MixRgb(a: MgTuple_RGB, b: MgTuple_RGB, t: f32) -> MgTuple_RGB {", wgsl);
    }

    [Fact]
    public void Byte_Subtraction_Promotes_To_I32_Not_U32_Underflow()
    {
        // C# widens `b.R - a.R` (byte-byte) to int; WGSL u32 subtraction would
        // underflow when b<a. The emitter must insert i32() on each operand.
        var wgsl = Wgsl(MixRgb);
        Assert.Contains("i32(b.R) - i32(a.R)", wgsl);
    }

    [Fact]
    public void Mixed_Double_Expression_Lifts_Byte_To_F32()
    {
        var wgsl = Wgsl(MixRgb);
        // a.R (byte) added into a double expression becomes f32(a.R).
        Assert.Contains("f32(a.R) +", wgsl);
    }

    [Fact]
    public void Byte_Cast_Truncates_And_Masks()
    {
        var wgsl = Wgsl(MixRgb);
        Assert.Contains("u32(", wgsl);
        Assert.Contains("& 0xffu)", wgsl);
        Assert.Contains("let r: u32 =", wgsl);
    }

    [Fact]
    public void Tuple_Literal_Returns_Struct_Constructor()
    {
        var wgsl = Wgsl(MixRgb);
        Assert.Contains("return MgTuple_RGB(r, g, bl);", wgsl);
    }

    [Fact]
    public void Early_Return_Of_Struct_Param()
    {
        var wgsl = Wgsl(MixRgb);
        Assert.Contains("if (t <= 0.0) {", wgsl);
        Assert.Contains("return a;", wgsl);
        Assert.Contains("if (t >= 1.0) {", wgsl);
        Assert.Contains("return b;", wgsl);
    }

    [Fact]
    public void Cross_Type_Const_Member_Folds_To_Literal()
    {
        // `Enc.BedrockTileId` (a byte const on another type) inlines to its
        // value — WGSL has no external named constants. The byte promotes to
        // i32 in the `int ==` comparison.
        var wgsl = TranspilerEngine.TranspileSourceToWgsl("""
            public static class Enc { public const byte BedrockTileId = 1; }
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static int Pick(int tile) {
                    if (tile == Enc.BedrockTileId) { return 7; }
                    return 0;
                }
            }
            """);
        Assert.Contains("if (tile == i32(1u)) {", wgsl);
        Assert.DoesNotContain("Enc.BedrockTileId", wgsl);
    }

    [Fact]
    public void Uninitialized_Local_Emits_Zero_Init_Var()
    {
        // A C# local declared without an initializer and assigned in branches
        // becomes a WGSL function-scope `var x: T;` (zero-initialized).
        var wgsl = Wgsl("""
            public static (byte R, byte G, byte B) Choose(bool hi, (byte R, byte G, byte B) a, (byte R, byte G, byte B) b) {
                (byte R, byte G, byte B) c;
                if (hi) { c = a; } else { c = b; }
                return c;
            }
            """);
        Assert.Contains("var c: MgTuple_RGB;", wgsl);
        Assert.Contains("c = a;", wgsl);
        Assert.Contains("c = b;", wgsl);
    }

    [Fact]
    public void For_Loop_Lowers_Postincrement()
    {
        var wgsl = Wgsl("""
            public static int SumTo(int n) {
                int sum = 0;
                for (int i = 0; i < n; i++) {
                    sum = sum + i;
                }
                return sum;
            }
            """);
        Assert.Contains("for (var i: i32 = 0; i < n; i = i + 1) {", wgsl);
        Assert.Contains("var sum: i32 = 0;", wgsl);
    }
}
