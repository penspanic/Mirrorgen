using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class InterpolationFormatTests
{
    static string TranspileBody(string body, string returnType, string paramList) =>
        TranspilerEngine.TranspileSource($$"""
            using System;
            public static class S {
                [Mirrorgen.Transpile]
                public static {{returnType}} F({{paramList}}) {
                    {{body}}
                }
            }
            """);

    [Fact]
    public void Uppercase_Hex_Pad_16_Maps_To_PadStart()
    {
        // ToString("X16") and $"{x:X16}" produce the same emit shape.
        var ts = TranspileBody(
            "return $\"high={x:X16}\";",
            returnType: "string",
            paramList: "ulong x");
        Assert.Contains("(x).toString(16).toUpperCase().padStart(16, '0')", ts);
    }

    [Fact]
    public void Lowercase_Hex_Skips_ToUpperCase()
    {
        var ts = TranspileBody(
            "return $\"high={x:x8}\";",
            returnType: "string",
            paramList: "ulong x");
        Assert.Contains("(x).toString(16).padStart(8, '0')", ts);
        Assert.DoesNotContain("toUpperCase", ts);
    }

    [Fact]
    public void Int_Source_Goes_Via_Number_To_String()
    {
        // BigInt path is only taken when the source is genuinely a bigint.
        var ts = TranspileBody(
            "return $\"i={x:X4}\";",
            returnType: "string",
            paramList: "int x");
        Assert.Contains("Number(x).toString(16).toUpperCase().padStart(4, '0')", ts);
    }

    [Fact]
    public void Decimal_Pad_Is_PadStart_String()
    {
        var ts = TranspileBody(
            "return $\"n={x:D5}\";",
            returnType: "string",
            paramList: "int x");
        Assert.Contains("String(x).padStart(5, '0')", ts);
    }

    [Fact]
    public void Plain_Hex_Without_Width_Skips_PadStart()
    {
        var ts = TranspileBody(
            "return $\"x={x:X}\";",
            returnType: "string",
            paramList: "int x");
        Assert.Contains("Number(x).toString(16).toUpperCase()", ts);
        Assert.DoesNotContain("padStart", ts);
    }

    [Fact]
    public void Unknown_Format_Specifier_Throws()
    {
        // F2 / N / etc. would need toFixed shims; refuse rather than silently
        // drop the format.
        var ex = Assert.Throws<System.NotSupportedException>(() =>
            TranspileBody("return $\"x={x:F2}\";", "string", "double x"));
        Assert.Contains("Unsupported interpolation format specifier 'F2'", ex.Message);
    }
}
