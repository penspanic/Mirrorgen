using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class MathInvocationTests
{
    static string Transpile(string body, string returnType = "int", string paramList = "int x, int y") =>
        TranspilerEngine.TranspileSource($$"""
            using System;
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static {{returnType}} F({{paramList}}) {
                    {{body}}
                }
            }
            """);

    [Fact]
    public void Math_Max_Emits_JS_Math_max()
    {
        var ts = Transpile("return Math.Max(x, y);");
        Assert.Contains("return Math.max(x, y);", ts);
    }

    [Fact]
    public void Math_Min_Emits_JS_Math_min()
    {
        var ts = Transpile("return Math.Min(x, y);");
        Assert.Contains("return Math.min(x, y);", ts);
    }

    [Fact]
    public void Math_Abs_Int_Wraps_To_Cover_IntMinValue_Edge()
    {
        // Math.abs(int.MinValue) in JS lands at 2^31, outside int32. The `| 0`
        // wrap brings it back to int.MinValue so C# (unchecked) and JS agree.
        var ts = Transpile("return Math.Abs(x);", paramList: "int x");
        Assert.Contains("return (Math.abs(x) | 0);", ts);
    }

    [Fact]
    public void Math_Abs_Double_Does_Not_Wrap()
    {
        var ts = Transpile("return Math.Abs(x);", returnType: "double", paramList: "double x");
        Assert.Contains("return Math.abs(x);", ts);
    }

    [Fact]
    public void Math_Sqrt_Emits_JS_Math_sqrt()
    {
        var ts = Transpile("return Math.Sqrt(x);", returnType: "double", paramList: "double x");
        Assert.Contains("return Math.sqrt(x);", ts);
    }

    [Fact]
    public void Math_Pow_Emits_JS_Math_pow()
    {
        var ts = Transpile("return Math.Pow(x, y);", returnType: "double", paramList: "double x, double y");
        Assert.Contains("return Math.pow(x, y);", ts);
    }

    [Fact]
    public void Math_Ceiling_Emits_JS_Math_ceil()
    {
        var ts = Transpile("return Math.Ceiling(x);", returnType: "double", paramList: "double x");
        Assert.Contains("return Math.ceil(x);", ts);
    }

    [Fact]
    public void MathF_Sin_Emits_JS_Math_sin()
    {
        var ts = Transpile("return MathF.Sin(x);", returnType: "float", paramList: "float x");
        Assert.Contains("return Math.sin(x);", ts);
    }

    [Fact]
    public void Math_Composed_With_Int_Arithmetic_Wraps()
    {
        var ts = Transpile("return Math.Max(x + 1, y) * 2;");
        // Outer multiply on int triggers Math.imul wrap; Math.max preserved.
        Assert.Contains("Math.imul(Math.max(((x + 1) | 0), y), 2)", ts);
    }

    [Fact]
    public void Unsupported_Math_Method_Falls_Through_To_Error()
    {
        Assert.Throws<NotSupportedException>(() => Transpile(
            "return Math.BigMul(x, y);",
            returnType: "long",
            paramList: "int x, int y"));
    }

    [Fact]
    public void NonMath_External_Method_Still_Rejected()
    {
        Assert.Throws<NotSupportedException>(() => Transpile(
            "return Console.Read();",
            returnType: "int",
            paramList: ""));
    }

    [Fact]
    public void Math_PI_Preserved_As_Named_Constant()
    {
        // Without the special case, the generic const-field inliner would
        // replace Math.PI with its literal value (3.141592653589793),
        // which is correct but unreadable on the TS side.
        var ts = Transpile("return Math.PI;", returnType: "double", paramList: "");
        Assert.Contains("return Math.PI;", ts);
        Assert.DoesNotContain("3.14159", ts);
    }

    [Fact]
    public void Math_E_Preserved_As_Named_Constant()
    {
        var ts = Transpile("return Math.E;", returnType: "double", paramList: "");
        Assert.Contains("return Math.E;", ts);
    }

    [Fact]
    public void MathF_PI_Also_Maps_To_Math_PI()
    {
        var ts = Transpile("return MathF.PI;", returnType: "float", paramList: "");
        Assert.Contains("return Math.PI;", ts);
    }
}
