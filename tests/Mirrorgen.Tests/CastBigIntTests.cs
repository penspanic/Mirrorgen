using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class CastBigIntTests
{
    static string Transpile(string body, string returnType, string paramList) =>
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
    public void ULong_From_Int_Wraps_With_BigInt_Ctor()
    {
        // (ulong)(s * s) emits `BigInt((s * s))` so subsequent bigint ops
        // (`* (ulong)other` → bigint*bigint) stay valid in TS.
        var ts = Transpile(
            "return (ulong)(s * s);",
            returnType: "ulong",
            paramList: "int s");
        Assert.Contains("return BigInt(", ts);
        Assert.Contains("Math.imul(s, s)", ts);
    }

    [Fact]
    public void Long_From_Int_Wraps_With_BigInt_Ctor()
    {
        var ts = Transpile(
            "return (long)x;",
            returnType: "long",
            paramList: "int x");
        Assert.Contains("return BigInt(x);", ts);
    }

    [Fact]
    public void Int_From_ULong_Collapses_Via_Number_AsIntN()
    {
        // (int)(1UL & t) — bigint result must collapse back to a JS number
        // with i32 reinterpretation so TS lets us assign to `let rx: number`.
        var ts = Transpile(
            "int r = (int)(1UL & t); return r;",
            returnType: "int",
            paramList: "ulong t");
        Assert.Contains("Number(BigInt.asIntN(32,", ts);
    }

    [Fact]
    public void Int_From_Long_Also_Collapses()
    {
        var ts = Transpile(
            "return (int)x;",
            returnType: "int",
            paramList: "long x");
        Assert.Contains("Number(BigInt.asIntN(32, x))", ts);
    }

    [Fact]
    public void BigInt_Shift_By_Int_Promotes_Shift_Amount()
    {
        // 1UL << (2 * level) — RHS is `int` arithmetic; TS BigInt insists on
        // a bigint shift amount, so the emit auto-wraps with BigInt(...).
        var ts = Transpile(
            "return 1UL << (2 * level);",
            returnType: "ulong",
            paramList: "int level");
        Assert.Contains("1n << BigInt(", ts);
        // No bare `<< Math.imul(...)` should leak through.
        Assert.DoesNotContain("1n << Math.imul", ts);
        Assert.DoesNotContain("1n << (2", ts);
    }

    [Fact]
    public void BigInt_RightShift_By_Int_Promotes_Too()
    {
        var ts = Transpile(
            "return t >> n;",
            returnType: "ulong",
            paramList: "ulong t, int n");
        Assert.Contains("t >> BigInt(n)", ts);
    }

    [Fact]
    public void BigInt_Shift_By_BigInt_Stays_Plain()
    {
        // No auto-wrap when both sides are already bigint.
        var ts = Transpile(
            "return t << n;",
            returnType: "ulong",
            paramList: "ulong t, ulong n");
        // Plain bigint << bigint — no BigInt() wrap on the RHS.
        Assert.Contains("t << n", ts);
        Assert.DoesNotContain("BigInt(n)", ts);
    }
}
