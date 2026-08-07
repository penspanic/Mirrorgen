using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class ThrowAndInterpTests
{
    static string Transpile(string body, string returnType = "void", string paramList = "int x") =>
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
    public void Throw_ArgumentOutOfRange_Maps_To_RangeError()
    {
        var ts = Transpile("if (x < 0) throw new ArgumentOutOfRangeException(nameof(x), \"x must be non-negative\");");
        Assert.Contains("throw new RangeError(\"x must be non-negative\");", ts);
    }

    [Fact]
    public void Throw_ArgumentException_Maps_To_TypeError()
    {
        var ts = Transpile("if (x < 0) throw new ArgumentException(\"bad\");");
        Assert.Contains("throw new TypeError(\"bad\");", ts);
    }

    [Fact]
    public void Throw_InvalidOperation_Falls_Back_To_Error()
    {
        var ts = Transpile("if (x < 0) throw new InvalidOperationException(\"oops\");");
        Assert.Contains("throw new Error(\"oops\");", ts);
    }

    [Fact]
    public void Throw_Strips_NameOf_Param_Argument()
    {
        // nameof(x) is the first arg in C#'s ArgumentOutOfRangeException
        // convention — the TS side only wants the message.
        var ts = Transpile("if (x < 0) throw new ArgumentOutOfRangeException(nameof(x), \"too small\");");
        Assert.DoesNotContain("nameof", ts);
        Assert.DoesNotContain("\"x\"", ts);
        Assert.Contains("\"too small\"", ts);
    }

    [Fact]
    public void Throw_With_Empty_Args_Emits_Empty_Message()
    {
        var ts = Transpile("if (x < 0) throw new InvalidOperationException();");
        Assert.Contains("throw new Error(\"\");", ts);
    }

    [Fact]
    public void InterpolatedString_Emits_Template_Literal()
    {
        var ts = Transpile(
            "return $\"x={x} y={y}\";",
            returnType: "string",
            paramList: "int x, int y");
        Assert.Contains("return `x=${x} y=${y}`;", ts);
    }

    [Fact]
    public void InterpolatedString_Escapes_Backtick_And_Dollar()
    {
        // Inside a C# interpolated string, both ` (backtick) and standalone
        // `$` are normal characters — but they have meaning inside a TS
        // template literal, so we have to escape them when emitting.
        var ts = Transpile(
            "return $\"price=`{x}` cost ${x}\";",
            returnType: "string",
            paramList: "int x");
        Assert.Contains("`price=\\`${x}\\` cost \\$${x}`", ts);
    }

    [Fact]
    public void Throw_With_Interpolated_Message_Combines()
    {
        var ts = Transpile(
            "if (x < 0) throw new ArgumentOutOfRangeException(nameof(x), $\"x={x} out of range\");",
            paramList: "int x");
        Assert.Contains("throw new RangeError(`x=${x} out of range`);", ts);
    }

    [Fact]
    public void InterpolatedString_AlignmentClause_Rejected()
    {
        Assert.Throws<NotSupportedException>(() => Transpile(
            "return $\"x={x,5}\";",
            returnType: "string",
            paramList: "int x"));
    }

    [Fact]
    public void InterpolatedString_FormatClause_Rejected()
    {
        Assert.Throws<NotSupportedException>(() => Transpile(
            "return $\"x={x:F2}\";",
            returnType: "string",
            paramList: "double x"));
    }
}
