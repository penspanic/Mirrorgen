using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class LoopTests
{
    static string Transpile(string body, string returnType = "int", string paramList = "int n") =>
        TranspilerEngine.TranspileSource($$"""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static {{returnType}} F({{paramList}}) {
                    {{body}}
                }
            }
            """);

    [Fact]
    public void For_Loop_C_Style_With_PostfixIncrement()
    {
        var ts = Transpile("""
            int sum = 0;
            for (int i = 0; i < n; i++) {
                sum += i;
            }
            return sum;
            """);
        Assert.Contains("for (let i: number = 0; i < n; i++) {", ts);
        Assert.Contains("    sum += i;", ts);
    }

    [Fact]
    public void For_Loop_With_Var()
    {
        var ts = Transpile("""
            int sum = 0;
            for (var i = 0; i < n; i++) sum += i;
            return sum;
            """);
        Assert.Contains("for (let i: number = 0; i < n; i++) {", ts);
    }

    [Fact]
    public void For_Loop_With_Compound_Increment()
    {
        var ts = Transpile("""
            int sum = 0;
            for (int i = 0; i < n; i += 2) sum += i;
            return sum;
            """);
        Assert.Contains("for (let i: number = 0; i < n; i += 2) {", ts);
    }

    [Fact]
    public void ForEach_Array_Becomes_For_Of()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static int Sum(int[] arr) {
                    int total = 0;
                    foreach (var x in arr) total += x;
                    return total;
                }
            }
            """);
        Assert.Contains("export function Sum(arr: number[]): number", ts);
        Assert.Contains("for (const x of arr) {", ts);
    }

    [Fact]
    public void ForEach_NonArray_Throws()
    {
        Assert.Throws<NotSupportedException>(() =>
            TranspilerEngine.TranspileSource("""
                using System.Collections.Generic;

                public static class S {
                    [Mirrorgen.Attributes.Transpile]
                    public static int Sum(List<int> arr) {
                        int total = 0;
                        foreach (var x in arr) total += x;
                        return total;
                    }
                }
                """));
    }

    [Fact]
    public void Postfix_Decrement_Emits_MinusMinus()
    {
        var ts = Transpile("""
            int x = n;
            x--;
            return x;
            """);
        Assert.Contains("x--;", ts);
    }
}
