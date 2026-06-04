using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

// W1 — WGSL backend scaffolding: scalar arithmetic functions (fn decl,
// params, return, locals as let/var, if/else-if, binary, double→f32,
// ternary→select). Composite types / arrays / uniforms come in W2/W3.
public class WgslScalarTests
{
    static string Wgsl(string members) =>
        TranspilerEngine.TranspileSourceToWgsl($$"""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                {{members}}
            }
            """);

    const string Smoothstep = """
        public static double Smoothstep(double edge0, double edge1, double x) {
            if (edge1 <= edge0) {
                return x >= edge1 ? 1d : 0d;
            }
            double t = (x - edge0) / (edge1 - edge0);
            if (t < 0d) t = 0d;
            else if (t > 1d) t = 1d;
            return t * t * (3d - 2d * t);
        }
        """;

    [Fact]
    public void Emits_Fn_Signature_With_F32()
    {
        var wgsl = Wgsl(Smoothstep);
        Assert.Contains("fn Smoothstep(edge0: f32, edge1: f32, x: f32) -> f32 {", wgsl);
    }

    [Fact]
    public void Lowers_Ternary_To_Select_FalseFirst()
    {
        var wgsl = Wgsl(Smoothstep);
        Assert.Contains("return select(0.0, 1.0, x >= edge1);", wgsl);
    }

    [Fact]
    public void Reassigned_Local_Is_Var_With_Float_Literals()
    {
        var wgsl = Wgsl(Smoothstep);
        Assert.Contains("var t: f32 = (x - edge0) / (edge1 - edge0);", wgsl);
    }

    [Fact]
    public void Emits_Else_If_Chain()
    {
        var wgsl = Wgsl(Smoothstep);
        Assert.Contains("if (t < 0.0) {", wgsl);
        Assert.Contains("} else if (t > 1.0) {", wgsl);
        Assert.Contains("t = 1.0;", wgsl);
    }

    [Fact]
    public void Float_Literals_Get_Decimal_Point()
    {
        var wgsl = Wgsl(Smoothstep);
        Assert.Contains("return t * t * (3.0 - 2.0 * t);", wgsl);
    }

    [Fact]
    public void Immutable_Local_Is_Let()
    {
        var wgsl = Wgsl("""
            public static double Half(double a, double b) {
                double m = (a + b) / 2d;
                return m;
            }
            """);
        Assert.Contains("let m: f32 = (a + b) / 2.0;", wgsl);
    }

    [Fact]
    public void Int_Function_Maps_To_I32()
    {
        var wgsl = Wgsl("""
            public static int AddClamp(int a, int b) {
                int s = a + b;
                if (s > 100) s = 100;
                return s;
            }
            """);
        Assert.Contains("fn AddClamp(a: i32, b: i32) -> i32 {", wgsl);
        Assert.Contains("var s: i32 = a + b;", wgsl);
        Assert.Contains("if (s > 100) {", wgsl);
    }
}
