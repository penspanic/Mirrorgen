using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

/// <summary>
/// Bitwise compound assignment (`^= &amp;= |= &lt;&lt;= &gt;&gt;=`) and the C# 11
/// unsigned right shift `&gt;&gt;&gt;`. Both used to fail the emit outright, which
/// forced bit-twiddling code — the shape Mirrorgen exists to mirror — into
/// longhand.
/// </summary>
public class BitwiseOperatorTests
{
    static string Transpile(string body) => TranspilerEngine.TranspileSource($$"""
        using Mirrorgen;
        public static class S {
            {{body}}
        }
        """);

    [Fact]
    public void Int32_Bitwise_Compound_Needs_No_Wrap()
    {
        // JS `& | ^` and `<< >>` already yield an int32 from int32 operands, so
        // the compound form is exact as written.
        var ts = Transpile("""
            [Transpile] public static int F(int a, int b) {
                int x = a;
                x ^= b;
                x &= b;
                x |= b;
                x <<= 3;
                x >>= 3;
                return x;
            }
            """);
        Assert.Contains("x = (x ^ b);", ts);
        Assert.Contains("x = (x & b);", ts);
        Assert.Contains("x = (x | b);", ts);
        Assert.Contains("x = (x << 3);", ts);
        Assert.Contains("x = (x >> 3);", ts);
    }

    [Fact]
    public void Int32_UnsignedShift_Compound_Returns_To_Int32()
    {
        var ts = Transpile("""
            [Transpile] public static int F(int a) { int x = a; x >>>= 3; return x; }
            """);
        Assert.Contains("x = ((x >>> 3) | 0);", ts);
    }

    [Fact]
    public void UInt32_Bitwise_Compound_Wraps()
    {
        var ts = Transpile("""
            [Transpile] public static uint F(uint a, uint b) {
                uint x = a;
                x ^= b;
                x &= b;
                x |= b;
                x <<= 3;
                x >>= 3;
                return x;
            }
            """);
        Assert.Contains("x = ((x ^ b) >>> 0);", ts);
        Assert.Contains("x = ((x & b) >>> 0);", ts);
        Assert.Contains("x = ((x | b) >>> 0);", ts);
        Assert.Contains("x = ((x << 3) >>> 0);", ts);
        // C# `>>` on uint is already logical.
        Assert.Contains("x = (x >>> 3);", ts);
    }

    [Fact]
    public void Int32_UnsignedShift_Wraps_So_Shift_By_Zero_Stays_Signed()
    {
        // The subtle one. C# `x >>> 0` is the identity — a negative x stays
        // negative. JS `x >>> 0` reinterprets as unsigned. `| 0` makes the two
        // agree without special-casing the shift count.
        var ts = Transpile("[Transpile] public static int F(int x, int n) => x >>> n;");
        Assert.Contains("return ((x >>> n) | 0);", ts);
    }

    [Fact]
    public void UInt32_UnsignedShift_Is_A_Plain_Logical_Shift()
    {
        var ts = Transpile("[Transpile] public static uint F(uint x, int n) => x >>> n;");
        Assert.Contains("return (x >>> n);", ts);
    }

    [Fact]
    public void Int64_Bitwise_Compound_Wraps_Through_BigInt()
    {
        var ts = Transpile("""
            [Transpile] public static long F(long a, long b) {
                long x = a;
                x ^= b;
                x <<= 3;
                return x;
            }
            """);
        Assert.Contains("x = BigInt.asIntN(64, x ^ b);", ts);
        // A C# shift count is an int; JS BigInt demands a bigint on both sides.
        Assert.Contains("x = BigInt.asIntN(64, x << BigInt(3 & 63));", ts);
    }

    [Fact]
    public void UnsignedShift_On_BigInt_Is_Rejected()
    {
        // BigInt has no `>>>` — an unsigned shift over an arbitrary-precision
        // integer is meaningless without a fixed width. Refuse rather than
        // emit something that looks right.
        var ex = Assert.Throws<NotSupportedException>(() =>
            Transpile("[Transpile] public static long F(long x, int n) => x >>> n;"));
        Assert.Contains("BigInt has no unsigned right shift", ex.Message);
    }

    [Fact]
    public void Xorshift32_Reads_Like_The_Textbook_Now()
    {
        // The whole point: `x ^= x << 13` used to be a build error, so the
        // reference implementation had to be written out longhand.
        var ts = Transpile("""
            [Transpile] public static uint Next(uint state) {
                uint x = state;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                return x;
            }
            """);
        Assert.Contains("x = ((x ^ ((x << 13) >>> 0)) >>> 0);", ts);
        Assert.Contains("x = ((x ^ (x >>> 17)) >>> 0);", ts);
        Assert.Contains("x = ((x ^ ((x << 5) >>> 0)) >>> 0);", ts);
    }
}
