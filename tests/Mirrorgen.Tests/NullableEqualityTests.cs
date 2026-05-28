using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class NullableEqualityTests
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
    public void Nullable_NotEqualsNull_Uses_Loose_Equality()
    {
        // The default-null param shape (`T? x = null` on the C# side) lowers
        // to TS `x?: T | null` (note `?:` AND `| null` — both optional and
        // nullable). Strict `!== null` lets undefined slip past, breaking
        // narrowing in the body. Loose `!= null` covers both sentinels.
        var ts = Transpile("return x != null;", returnType: "bool", paramList: "double? x");
        Assert.Contains("return x != null;", ts);
    }

    [Fact]
    public void Nullable_EqualsNull_Uses_Loose_Equality()
    {
        var ts = Transpile("return x == null;", returnType: "bool", paramList: "double? x");
        Assert.Contains("return x == null;", ts);
    }

    [Fact]
    public void NonNullable_StillUses_Strict_Equality_When_Compared_To_Other()
    {
        // Regression: int / int comparison should still emit `===` — the
        // loose-equality detour only applies to `== null` / `!= null` on a
        // nullable receiver.
        var ts = Transpile("return x == y;", returnType: "bool", paramList: "int x, int y");
        Assert.Contains("return x === y;", ts);
    }
}
