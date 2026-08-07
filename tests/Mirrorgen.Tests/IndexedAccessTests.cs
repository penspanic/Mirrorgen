using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

/// <summary>
/// Element reads carry a non-null assertion so the emit typechecks under
/// `noUncheckedIndexedAccess`, where `xs[i]` is `T | undefined`. The index was
/// already proven in-range on the C# side. Write targets must stay bare —
/// `xs[i]! = v` is a syntax error.
/// </summary>
public class IndexedAccessTests
{
    static string Transpile(string body) => TranspilerEngine.TranspileSource($$"""
        using System.Collections.Generic;
        using Mirrorgen;
        public static class S {
            {{body}}
        }
        """);

    [Fact]
    public void Array_Read_Asserts_Non_Null()
    {
        var ts = Transpile("[Transpile] public static int F(int[] xs, int i) => xs[i];");
        Assert.Contains("return xs[i]!;", ts);
    }

    [Fact]
    public void Array_Write_Stays_Bare()
    {
        var ts = Transpile("""
            [Transpile] public static int[] F(int n) {
                var xs = new int[n];
                for (int i = 0; i < n; i++) xs[i] = i;
                return xs;
            }
            """);
        Assert.Contains("xs[i] = i;", ts);
        Assert.DoesNotContain("xs[i]! =", ts);
    }

    [Fact]
    public void Compound_Write_Stays_Bare_On_The_Left_And_Asserts_On_The_Right()
    {
        // `xs[i] += v` expands to `xs[i] = ((xs[i] + v) | 0)` — the left is a
        // write target, the right is a read.
        var ts = Transpile("""
            [Transpile] public static int[] F(int[] xs, int n) {
                for (int i = 0; i < n; i++) xs[i] += 1;
                return xs;
            }
            """);
        Assert.Contains("xs[i] = ((xs[i]! + 1) | 0);", ts);
    }

    [Fact]
    public void Increment_On_An_Element_Stays_Bare()
    {
        var ts = Transpile("""
            [Transpile] public static int[] F(int[] xs, int n) {
                for (int i = 0; i < n; i++) xs[i]++;
                return xs;
            }
            """);
        Assert.Contains("xs[i]++;", ts);
        Assert.DoesNotContain("xs[i]!++", ts);
    }

    [Fact]
    public void Dictionary_Read_Asserts_Too()
    {
        // A Record<K,V> index is just as `undefined`-typed as an array's.
        var ts = Transpile("[Transpile] public static int F(Dictionary<string, int> d, string k) => d[k];");
        Assert.Contains("return d[k]!;", ts);
    }

    [Fact]
    public void Nested_Read_Asserts_At_Both_Levels()
    {
        var ts = Transpile("[Transpile] public static int F(int[] xs, int[] idx, int i) => xs[idx[i]];");
        Assert.Contains("return xs[idx[i]!]!;", ts);
    }
}
