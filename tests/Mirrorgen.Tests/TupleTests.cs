using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class TupleTests
{
    static string Transpile(string body, string returnType, string paramList = "int x, int y") =>
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
    public void NamedTupleReturn_TypeEmits_As_ObjectType()
    {
        var ts = Transpile("return (IX: x, IY: y);", "(int IX, int IY)");
        Assert.Contains("F(x: number, y: number): { IX: number; IY: number }", ts);
    }

    [Fact]
    public void NamedTupleReturn_ExpressionEmits_With_Names_From_TargetType()
    {
        // Expression is positional `return (x, y)` — names come from the
        // method's declared return type, not the literal.
        var ts = Transpile("return (x, y);", "(int IX, int IY)");
        Assert.Contains("return { IX: x, IY: y };", ts);
    }

    [Fact]
    public void NamedTupleReturn_With_Inline_Names_Wins()
    {
        var ts = Transpile("return (Foo: x, Bar: y);", "(int IX, int IY)");
        Assert.Contains("return { Foo: x, Bar: y };", ts);
    }

    [Fact]
    public void UnnamedTupleReturn_EmitsAs_PositionalArray()
    {
        var ts = Transpile("return (x, y);", "(int, int)");
        Assert.Contains("F(x: number, y: number): [number, number]", ts);
        Assert.Contains("return [x, y];", ts);
    }

    [Fact]
    public void Tuple_With_Mixed_Numeric_Element_Types()
    {
        var ts = Transpile(
            "return (Hi: (ulong)x, Lo: y);",
            "(ulong Hi, int Lo)",
            "int x, int y");
        Assert.Contains(": { Hi: bigint; Lo: number }", ts);
    }
}
