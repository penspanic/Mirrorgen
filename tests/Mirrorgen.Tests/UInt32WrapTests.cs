using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

/// <summary>
/// uint32 has no JS counterpart: `+ - * /` produce an unbounded double and the
/// bitwise / shift operators produce a *signed* int32. Every uint-typed result
/// therefore needs an explicit `>>> 0`. Before this, `(uint)0 - 1` emitted as
/// `-1` (C# gives 4294967295) and `7u / 2u` emitted as `3.5` (C# gives 3).
/// </summary>
public class UInt32WrapTests
{
    static string Transpile(string body) => TranspilerEngine.TranspileSource($$"""
        using Mirrorgen;
        public static class S {
            {{body}}
        }
        """);

    [Fact]
    public void Add_And_Sub_Wrap_With_Unsigned_Shift()
    {
        var ts = Transpile("""
            [Transpile] public static uint Add(uint a, uint b) => a + b;
            [Transpile] public static uint Sub(uint a, uint b) => a - b;
            """);
        Assert.Contains("return ((a + b) >>> 0);", ts);
        Assert.Contains("return ((a - b) >>> 0);", ts);
    }

    [Fact]
    public void Mul_Uses_Imul_Then_Wraps()
    {
        // Math.imul keeps the low 32 bits exact past 2^53; its result is
        // signed, so the wrap still has to follow.
        var ts = Transpile("[Transpile] public static uint Mul(uint a, uint b) => a * b;");
        Assert.Contains("return (Math.imul(a, b) >>> 0);", ts);
    }

    [Fact]
    public void Div_Truncates_Instead_Of_Producing_A_Float()
    {
        // The headline case: C# `7u / 2u` is 3, a bare JS `a / b` is 3.5.
        var ts = Transpile("[Transpile] public static uint Div(uint a, uint b) => a / b;");
        Assert.Contains("return ((a / b) >>> 0);", ts);
        Assert.DoesNotContain("return a / b;", ts);
    }

    [Fact]
    public void Mod_Wraps()
    {
        var ts = Transpile("[Transpile] public static uint Mod(uint a, uint b) => a % b;");
        Assert.Contains("return ((a % b) >>> 0);", ts);
    }

    [Fact]
    public void RightShift_Is_Logical_Not_Arithmetic()
    {
        // C# `>>` on uint is a logical shift. JS `>>` sign-extends anything at
        // or above 2^31 — 0x80000000 >> 17 would come out negative.
        var ts = Transpile("[Transpile] public static uint Shr(uint a, int n) => a >> n;");
        Assert.Contains("return (a >>> n);", ts);
    }

    [Fact]
    public void LeftShift_Wraps()
    {
        var ts = Transpile("[Transpile] public static uint Shl(uint a, int n) => a << n;");
        Assert.Contains("return ((a << n) >>> 0);", ts);
    }

    [Fact]
    public void Bitwise_Operators_Reinterpret_As_Unsigned()
    {
        var ts = Transpile("""
            [Transpile] public static uint And(uint a, uint b) => a & b;
            [Transpile] public static uint Or(uint a, uint b) => a | b;
            [Transpile] public static uint Xor(uint a, uint b) => a ^ b;
            """);
        Assert.Contains("return ((a & b) >>> 0);", ts);
        Assert.Contains("return ((a | b) >>> 0);", ts);
        Assert.Contains("return ((a ^ b) >>> 0);", ts);
    }

    [Fact]
    public void Complement_Reinterprets_As_Unsigned()
    {
        var ts = Transpile("[Transpile] public static uint Not(uint a) => ~a;");
        Assert.Contains("return ((~a) >>> 0);", ts);
    }

    [Fact]
    public void Compound_Assignment_Wraps()
    {
        var ts = Transpile("""
            [Transpile] public static uint F(uint a, uint b) {
                uint x = a;
                x += b;
                x -= b;
                x *= b;
                x /= b;
                x %= b;
                return x;
            }
            """);
        Assert.Contains("x = ((x + b) >>> 0);", ts);
        Assert.Contains("x = ((x - b) >>> 0);", ts);
        Assert.Contains("x = (Math.imul(x, b) >>> 0);", ts);
        Assert.Contains("x = ((x / b) >>> 0);", ts);
        Assert.Contains("x = ((x % b) >>> 0);", ts);
    }

    [Fact]
    public void PostIncrement_Expands_To_Wrapped_Assignment()
    {
        // JS `x++` keeps counting past 2^32; C# wraps to 0.
        var ts = Transpile("""
            [Transpile] public static uint F(uint a) { uint x = a; x++; x--; return x; }
            """);
        Assert.Contains("x = ((x + 1) >>> 0);", ts);
        Assert.Contains("x = ((x - 1) >>> 0);", ts);
    }

    [Fact]
    public void PostIncrement_In_For_Incrementor_Expands()
    {
        var ts = Transpile("""
            [Transpile] public static uint F(uint n) {
                uint sum = 0;
                for (uint i = 0; i < n; i++) sum += i;
                return sum;
            }
            """);
        Assert.Contains("i = ((i + 1) >>> 0)", ts);
    }

    [Fact]
    public void PostIncrement_Used_As_A_Value_Is_Rejected()
    {
        // The expansion yields the new value; `x++` yields the old one. Rather
        // than emit something subtly different, refuse it.
        var ex = Assert.Throws<NotSupportedException>(() => Transpile(
            "[Transpile] public static uint F(uint a) { uint x = a; uint y = x++; return y; }"));
        Assert.Contains("post-increment", ex.Message);
    }

    [Fact]
    public void Comparisons_Are_Left_Alone()
    {
        // Once both operands are honest uint32 numbers, a plain compare is
        // already correct — wrapping here would only add noise.
        var ts = Transpile("[Transpile] public static bool Lt(uint a, uint b) => a < b;");
        Assert.Contains("return a < b;", ts);
    }

    [Fact]
    public void Int32_Emit_Is_Unchanged()
    {
        // Regression guard: the uint branch must not capture int32.
        var ts = Transpile("""
            [Transpile] public static int Add(int a, int b) => a + b;
            [Transpile] public static int Mul(int a, int b) => a * b;
            [Transpile] public static int Shr(int a, int n) => a >> n;
            """);
        Assert.Contains("return ((a + b) | 0);", ts);
        Assert.Contains("return Math.imul(a, b);", ts);
        Assert.Contains("return a >> n;", ts);
        Assert.DoesNotContain(">>> 0", ts);
    }

    [Fact]
    public void Xorshift32_Emits_The_Textbook_Form_Correctly()
    {
        // The shape that started this: a uint-state xorshift32 used to emit
        // arithmetic shifts and unwrapped xors, diverging on every state at or
        // above 2^31.
        var ts = Transpile("""
            [Transpile] public static uint Next(uint state) {
                uint x = state;
                x = x ^ (x << 13);
                x = x ^ (x >> 17);
                x = x ^ (x << 5);
                return x;
            }
            """);
        Assert.Contains("x = ((x ^ (((x << 13) >>> 0))) >>> 0);", ts);
        Assert.Contains("x = ((x ^ ((x >>> 17))) >>> 0);", ts);
        Assert.Contains("x = ((x ^ (((x << 5) >>> 0))) >>> 0);", ts);
    }
}
