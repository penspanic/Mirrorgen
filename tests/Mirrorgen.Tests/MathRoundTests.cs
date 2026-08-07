using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class MathRoundTests
{
    [Fact]
    public void Math_Round_Single_Arg_Uses_Bankers_Helper()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            public static class S {
                [Mirrorgen.Transpile]
                public static double F(double x) => Math.Round(x);
            }
            """);
        Assert.Contains("function __mirrorgen_bankersRound", ts);
        Assert.Contains("return __mirrorgen_bankersRound(x);", ts);
    }

    [Fact]
    public void Math_Round_AwayFromZero_Uses_Distinct_Helper()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            public static class S {
                [Mirrorgen.Transpile]
                public static double F(double x) => Math.Round(x, MidpointRounding.AwayFromZero);
            }
            """);
        Assert.Contains("function __mirrorgen_awayFromZeroRound", ts);
        Assert.Contains("return __mirrorgen_awayFromZeroRound(x);", ts);
        Assert.DoesNotContain("function __mirrorgen_bankersRound", ts);
    }

    [Fact]
    public void Math_Truncate_Maps_To_Math_trunc_Without_Helper()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            public static class S {
                [Mirrorgen.Transpile]
                public static double F(double x) => Math.Truncate(x);
            }
            """);
        Assert.Contains("return Math.trunc(x);", ts);
        Assert.DoesNotContain("__mirrorgen_", ts);
    }

    [Fact]
    public void Helper_Emitted_Once_When_Used_Twice()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            public static class S {
                [Mirrorgen.Transpile]
                public static double F(double x) => Math.Round(x);
                [Mirrorgen.Transpile]
                public static double G(double x) => Math.Round(x);
            }
            """);
        var firstIndex = ts.IndexOf("function __mirrorgen_bankersRound");
        var lastIndex = ts.LastIndexOf("function __mirrorgen_bankersRound");
        Assert.True(firstIndex >= 0, "helper should appear at least once");
        Assert.Equal(firstIndex, lastIndex); // exactly one occurrence
    }

    [Fact]
    public void Round_With_Digits_Throws()
    {
        Assert.Throws<System.NotSupportedException>(() =>
            TranspilerEngine.TranspileSource("""
                using System;
                public static class S {
                    [Mirrorgen.Transpile]
                    public static double F(double x) => Math.Round(x, 2);
                }
                """));
    }
}
