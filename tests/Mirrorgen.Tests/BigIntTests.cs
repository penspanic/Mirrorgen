using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class BigIntTests
{
    [Fact]
    public void Long_Parameter_Emits_Bigint()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static long Identity(long x) => x;
            }
            """);
        Assert.Contains("export function Identity(x: bigint): bigint {", ts);
    }

    [Fact]
    public void Long_Literal_Emits_With_N_Suffix()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static long Five() => 5L;
            }
            """);
        Assert.Contains("return 5n;", ts);
    }

    [Fact]
    public void Long_Arithmetic_Wraps_With_AsIntN()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static long Sum(long a, long b) => a + b;
            }
            """);
        Assert.Contains("return BigInt.asIntN(64, a + b);", ts);
    }

    [Fact]
    public void Long_Multiplication_Uses_BigInt_Times()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static long Mul(long a, long b) => a * b;
            }
            """);
        Assert.Contains("return BigInt.asIntN(64, a * b);", ts);
    }

    [Fact]
    public void Ulong_Arithmetic_Wraps_With_AsUintN()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static ulong Sum(ulong a, ulong b) => a + b;
            }
            """);
        Assert.Contains("return BigInt.asUintN(64, a + b);", ts);
    }

    [Fact]
    public void Long_Property_On_Record_Emits_Bigint()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public record Timestamp(long Ticks);
            """);
        Assert.Contains("  Ticks: bigint;", ts);
    }

    [Fact]
    public void Long_Compound_Assignment_Wraps()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static long Total(long n) {
                    long total = 0L;
                    total += n;
                    return total;
                }
            }
            """);
        Assert.Contains("total = BigInt.asIntN(64, total + n);", ts);
    }
}
