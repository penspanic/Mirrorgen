using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class OperatorTests
{
    static string Transpile(string body, string returnType = "int", string paramList = "int x, int y") =>
        TranspilerEngine.TranspileSource($$"""
            public static class S {
                [Mirrorgen.Transpile]
                public static {{returnType}} F({{paramList}}) => {{body}};
            }
            """);

    [Fact] public void Add() => Assert.Contains("return ((x + y) | 0);", Transpile("x + y"));
    [Fact] public void Subtract() => Assert.Contains("return ((x - y) | 0);", Transpile("x - y"));
    [Fact] public void Multiply() => Assert.Contains("return Math.imul(x, y);", Transpile("x * y"));
    [Fact] public void Divide() => Assert.Contains("return ((x / y) | 0);", Transpile("x / y"));
    [Fact] public void Modulo() => Assert.Contains("return ((x % y) | 0);", Transpile("x % y"));

    [Fact]
    public void Equality_BecomesStrict() =>
        Assert.Contains("return x === y;", Transpile("x == y", returnType: "bool"));

    [Fact]
    public void Inequality_BecomesStrict() =>
        Assert.Contains("return x !== y;", Transpile("x != y", returnType: "bool"));

    [Fact] public void LessThan() => Assert.Contains("return x < y;", Transpile("x < y", returnType: "bool"));
    [Fact] public void GreaterThanEquals() => Assert.Contains("return x >= y;", Transpile("x >= y", returnType: "bool"));

    [Fact]
    public void LogicalAnd_Or() =>
        Assert.Contains(
            "return a && b || a;",
            Transpile("a && b || a", returnType: "bool", paramList: "bool a, bool b"));

    [Fact]
    public void LogicalNot() =>
        Assert.Contains("return !a;", Transpile("!a", returnType: "bool", paramList: "bool a"));

    [Fact] public void UnaryMinus() => Assert.Contains("return -x;", Transpile("-x", paramList: "int x"));

    [Fact]
    public void Ternary() =>
        Assert.Contains(
            "return x > 0 ? 1 : -1;",
            Transpile("x > 0 ? 1 : -1", paramList: "int x"));

    [Fact]
    public void Parenthesized_Precedence_Preserved() =>
        Assert.Contains("Math.imul(", Transpile("(x + y) * x"));

    [Fact]
    public void Int_Wrap_Is_Fully_Parenthesised() =>
        // `|` binds looser than comparison operators, so the wrap must be paren-wrapped:
        // (a + b) | 0 <= c   would mis-parse as   (a + b) | (0 <= c)
        Assert.Contains("((x + y) | 0)", Transpile("x + y"));

    [Fact]
    public void Double_Arithmetic_Not_Wrapped() =>
        Assert.Contains(
            "return a + b;",
            Transpile("a + b", returnType: "double", paramList: "double a, double b"));

    [Fact]
    public void Double_Multiplication_Not_Wrapped() =>
        Assert.Contains(
            "return a * b;",
            Transpile("a * b", returnType: "double", paramList: "double a, double b"));

    [Fact]
    public void Mixed_Int_And_Double_Promotes_To_Double_No_Wrap() =>
        Assert.Contains(
            "return x + 1.5;",
            Transpile("x + 1.5", returnType: "double", paramList: "int x"));

    [Fact]
    public void Bitwise_Operators_PassThrough()
    {
        Assert.Contains("return x & y;", Transpile("x & y"));
        Assert.Contains("return x | y;", Transpile("x | y"));
        Assert.Contains("return x ^ y;", Transpile("x ^ y"));
        Assert.Contains("return x << y;", Transpile("x << y"));
        Assert.Contains("return x >> y;", Transpile("x >> y"));
    }

    [Fact]
    public void NullCoalescing_On_Nullable_Maps_To_JS_NullishOperator() =>
        Assert.Contains(
            "return x ?? 0;",
            Transpile("x ?? 0", returnType: "double", paramList: "double? x"));
}
