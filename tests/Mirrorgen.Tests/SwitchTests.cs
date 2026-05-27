using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class SwitchTests
{
    [Fact]
    public void Switch_Statement_With_Constant_Cases_And_Default()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static string F(int x) {
                    switch (x) {
                        case 1: return "a";
                        case 2: return "b";
                        default: return "c";
                    }
                }
            }
            """);
        Assert.Contains("switch (x) {", ts);
        Assert.Contains("case 1:", ts);
        Assert.Contains("    return \"a\";", ts);
        Assert.Contains("case 2:", ts);
        Assert.Contains("default:", ts);
        Assert.Contains("    return \"c\";", ts);
    }

    [Fact]
    public void Switch_Statement_With_Enum_Case_Pattern()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public enum Color { Red, Green, Blue }

            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static int F(Color c) {
                    switch (c) {
                        case Color.Red: return 1;
                        case Color.Green: return 2;
                        case Color.Blue: return 3;
                        default: return 0;
                    }
                }
            }
            """);
        Assert.Contains("case Color.Red:", ts);
        Assert.Contains("case Color.Green:", ts);
        Assert.Contains("case Color.Blue:", ts);
    }

    [Fact]
    public void Switch_Statement_With_Break_Roundtrips()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static int F(int x) {
                    int r = 0;
                    switch (x) {
                        case 1: r = 10; break;
                        case 2: r = 20; break;
                        default: r = -1; break;
                    }
                    return r;
                }
            }
            """);
        Assert.Contains("case 1:", ts);
        Assert.Contains("    r = 10;", ts);
        Assert.Contains("    break;", ts);
    }

    [Fact]
    public void Switch_Expression_Emits_IIFE_With_If_Returns()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static string F(int x) {
                    return x switch {
                        1 => "a",
                        2 => "b",
                        _ => "c",
                    };
                }
            }
            """);
        Assert.Contains("((): string => {", ts);
        Assert.Contains("if (x === 1) return \"a\";", ts);
        Assert.Contains("if (x === 2) return \"b\";", ts);
        Assert.Contains("return \"c\";", ts);
    }

    [Fact]
    public void Switch_Expression_Over_Enum_Member()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public enum Tier { Bronze, Silver, Gold }

            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static int F(Tier t) {
                    return t switch {
                        Tier.Bronze => 100,
                        Tier.Silver => 200,
                        Tier.Gold => 500,
                        _ => 0,
                    };
                }
            }
            """);
        Assert.Contains("if (t === Tier.Bronze) return 100;", ts);
        Assert.Contains("if (t === Tier.Silver) return 200;", ts);
        Assert.Contains("if (t === Tier.Gold) return 500;", ts);
        Assert.Contains("return 0;", ts);
    }

    [Fact]
    public void Switch_Expression_Without_Discard_Throws_At_Runtime()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static int F(int x) {
                    return x switch {
                        1 => 10,
                        2 => 20,
                    };
                }
            }
            """);
        Assert.Contains("throw new Error(\"switch expression: no arm matched\");", ts);
    }

    [Fact]
    public void Switch_Var_Pattern_Throws_NotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            TranspilerEngine.TranspileSource("""
                public static class S {
                    [Mirrorgen.Attributes.Transpile]
                    public static int F(int x) {
                        return x switch {
                            int n when n > 0 => 1,
                            _ => 0,
                        };
                    }
                }
                """));
    }
}
