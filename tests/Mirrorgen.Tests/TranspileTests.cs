using Microsoft.CodeAnalysis.CSharp;
using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class TranspileTests
{
    [Fact]
    public void IntLiteral_Return_ExpressionBody()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static int Answer() => 42;
            }
            """);
        Assert.Contains("export function Answer(): number", ts);
        Assert.Contains("return 42;", ts);
    }

    [Fact]
    public void BoolLiteral_Return()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static bool Yes() => true;
            }
            """);
        Assert.Contains("export function Yes(): boolean", ts);
        Assert.Contains("return true;", ts);
    }

    [Fact]
    public void StringLiteral_Return()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static string Greet() => "hello";
            }
            """);
        Assert.Contains("export function Greet(): string", ts);
        Assert.Contains("return \"hello\";", ts);
    }

    [Fact]
    public void DoubleLiteral_UsesInvariantCulture()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static double Pi() => 3.14;
            }
            """);
        Assert.Contains("return 3.14;", ts);
        Assert.DoesNotContain("3,14", ts);
    }

    [Fact]
    public void Parameter_Identifier_PassesThrough()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static int Identity(int x) => x;
            }
            """);
        Assert.Contains("export function Identity(x: number): number", ts);
        Assert.Contains("return x;", ts);
    }

    [Fact]
    public void BlockBody_WithReturn()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static int Five() { return 5; }
            }
            """);
        Assert.Contains("return 5;", ts);
    }

    [Fact]
    public void Method_Without_Attribute_Is_Skipped()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                public static int Hidden() => 99;
            }
            """);
        Assert.DoesNotContain("Hidden", ts);
        Assert.DoesNotContain("99", ts);
    }

    [Fact]
    public void Short_Form_Attribute_Without_Namespace_Is_Recognized()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen.Attributes;
            public static class S {
                [Transpile]
                public static int A() => 1;
            }
            """);
        Assert.Contains("export function A(): number", ts);
    }

    [Fact]
    public void Void_Return_Empty_Return_Statement()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static void Noop() { return; }
            }
            """);
        Assert.Contains("export function Noop(): void", ts);
        Assert.Contains("return;", ts);
    }

    [Fact]
    public void Unsupported_Type_Throws()
    {
        // `char` remains unsupported — TS has no native single-character type.
        // decimal is now mapped to `number` for TsGen wire parity (v0.3).
        Assert.Throws<NotSupportedException>(() =>
            TranspilerEngine.TranspileSource("""
                public static class S {
                    [Mirrorgen.Attributes.Transpile]
                    public static char Initial() => 'A';
                }
                """));
    }
}
