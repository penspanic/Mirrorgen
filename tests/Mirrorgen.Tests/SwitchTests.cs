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
                [Mirrorgen.Transpile]
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
            [Mirrorgen.Transpile]
            public enum Color { Red, Green, Blue }

            public static class S {
                [Mirrorgen.Transpile]
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
                [Mirrorgen.Transpile]
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
                [Mirrorgen.Transpile]
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
        Assert.Contains("const _v = x;", ts);
        Assert.Contains("if (_v === 1) return \"a\";", ts);
        Assert.Contains("if (_v === 2) return \"b\";", ts);
        Assert.Contains("return \"c\";", ts);
    }

    [Fact]
    public void Switch_Expression_Over_Enum_Member()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public enum Tier { Bronze, Silver, Gold }

            public static class S {
                [Mirrorgen.Transpile]
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
        Assert.Contains("const _v = t;", ts);
        Assert.Contains("if (_v === Tier.Bronze) return 100;", ts);
        Assert.Contains("if (_v === Tier.Silver) return 200;", ts);
        Assert.Contains("if (_v === Tier.Gold) return 500;", ts);
        Assert.Contains("return 0;", ts);
    }

    [Fact]
    public void Switch_Expression_Without_Discard_Throws_At_Runtime()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
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
    public void Switch_Type_Pattern_Binds_And_Tests_Guard()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static int F(int x) {
                    return x switch {
                        int n when n > 0 => n * 2,
                        _ => 0,
                    };
                }
            }
            """);
        Assert.Contains("{ const n = _v;", ts);
        Assert.Contains("if (n > 0) return Math.imul(n, 2);", ts);
    }

    [Fact]
    public void Switch_Var_Pattern_Binds_Without_Guard()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static int F(int x) {
                    return x switch {
                        var n => n,
                    };
                }
            }
            """);
        Assert.Contains("{ const n = _v;", ts);
        Assert.Contains("return n;", ts);
    }

    [Fact]
    public void Switch_Relational_Pattern_Emits_Comparison()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static string F(int x) {
                    return x switch {
                        > 0 => "pos",
                        < 0 => "neg",
                        _ => "zero",
                    };
                }
            }
            """);
        Assert.Contains("if (_v > 0) return \"pos\";", ts);
        Assert.Contains("if (_v < 0) return \"neg\";", ts);
        Assert.Contains("return \"zero\";", ts);
    }

    [Fact]
    public void Switch_And_Pattern_Combines_Two_Relationals()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static string F(int x) {
                    return x switch {
                        > 0 and < 10 => "single",
                        _ => "other",
                    };
                }
            }
            """);
        Assert.Contains("(_v > 0 && _v < 10)", ts);
    }

    [Fact]
    public void Switch_Or_Pattern_Combines_Two_Constants()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static string F(int x) {
                    return x switch {
                        1 or 2 => "low",
                        _ => "other",
                    };
                }
            }
            """);
        Assert.Contains("(_v === 1 || _v === 2)", ts);
    }

    [Fact]
    public void Switch_Statement_With_Relational_Rewrites_To_If_Else()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static int F(int x) {
                    switch (x) {
                        case > 0: return 1;
                        case < 0: return -1;
                        default: return 0;
                    }
                }
            }
            """);
        Assert.Contains("{ const _v = x;", ts);
        Assert.Contains("if (_v > 0) {", ts);
        Assert.Contains("else if (_v < 0) {", ts);
        Assert.Contains("else {", ts);
    }

    [Fact]
    public void Switch_Statement_Constant_Labels_Keep_TS_Switch_Shape()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static int F(int x) {
                    switch (x) {
                        case 1: return 10;
                        case 2: return 20;
                        default: return 0;
                    }
                }
            }
            """);
        // Pure-constant labels keep the original TS switch — no const _v rewrite.
        Assert.Contains("switch (x) {", ts);
        Assert.Contains("case 1:", ts);
        Assert.DoesNotContain("const _v = x;", ts);
    }

    [Fact]
    public void Switch_Arm_With_When_Guard_Wraps_Both_Conditions()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static int F(int x, int limit) {
                    return x switch {
                        > 0 when x < limit => 1,
                        _ => 0,
                    };
                }
            }
            """);
        Assert.Contains("(_v > 0) && (x < limit)", ts);
    }
}
