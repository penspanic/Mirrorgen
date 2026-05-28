using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class DoubleSingleInvocationTests
{
    static string Transpile(string body, string returnType = "bool", string paramList = "double x") =>
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
    public void Double_IsNaN_Maps_To_Number_isNaN()
    {
        var ts = Transpile("return double.IsNaN(x);");
        Assert.Contains("return Number.isNaN(x);", ts);
    }

    [Fact]
    public void Double_IsFinite_Maps_To_Number_isFinite()
    {
        var ts = Transpile("return double.IsFinite(x);");
        Assert.Contains("return Number.isFinite(x);", ts);
    }

    [Fact]
    public void Double_IsInfinity_Maps_To_NotFinite_And_NotNaN()
    {
        var ts = Transpile("return double.IsInfinity(x);");
        Assert.Contains("return (!Number.isFinite(x) && !Number.isNaN(x));", ts);
    }

    [Fact]
    public void Double_IsPositiveInfinity_Maps_To_POSITIVE_INFINITY_Compare()
    {
        var ts = Transpile("return double.IsPositiveInfinity(x);");
        Assert.Contains("return (x === Number.POSITIVE_INFINITY);", ts);
    }

    [Fact]
    public void Double_IsNegativeInfinity_Maps_To_NEGATIVE_INFINITY_Compare()
    {
        var ts = Transpile("return double.IsNegativeInfinity(x);");
        Assert.Contains("return (x === Number.NEGATIVE_INFINITY);", ts);
    }

    [Fact]
    public void Float_IsNaN_Also_Maps()
    {
        var ts = Transpile("return float.IsNaN(x);", paramList: "float x");
        Assert.Contains("return Number.isNaN(x);", ts);
    }

    [Fact]
    public void Float_IsInfinity_Also_Maps()
    {
        var ts = Transpile("return float.IsInfinity(x);", paramList: "float x");
        Assert.Contains("return (!Number.isFinite(x) && !Number.isNaN(x));", ts);
    }

    [Fact]
    public void Double_IsInfinity_Threads_Compound_Guard_Naturally()
    {
        // The exact shape that triggered Section E in tidemark — a finite-and-positive
        // guard around a sqrt magnitude. Mirrorgen should walk it without complaint.
        var ts = Transpile(
            "if (!(x > 0d) || double.IsInfinity(x)) return false; return true;",
            returnType: "bool",
            paramList: "double x");
        Assert.Contains("if (!(x > 0) || (!Number.isFinite(x) && !Number.isNaN(x)))", ts);
    }

    [Fact]
    public void Double_IsNormal_Still_Rejected()
    {
        // We deliberately don't map IsNormal / IsSubnormal — JS has no precise
        // equivalent. Stays in the "unsupported BCL method" rejection path.
        Assert.Throws<NotSupportedException>(() =>
            Transpile("return double.IsNormal(x);"));
    }
}
