using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class ListMutationTests
{
    [Fact]
    public void New_ListInt_Emits_Empty_Array_Literal()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Collections.Generic;
            public static class S {
                [Mirrorgen.Transpile]
                public static int F() {
                    var xs = new List<int>();
                    return xs.Count;
                }
            }
            """);
        Assert.Contains("let xs: number[] = [];", ts);
        Assert.Contains("return xs.length;", ts);
    }

    [Fact]
    public void ListAdd_Emits_Push()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Collections.Generic;
            public static class S {
                [Mirrorgen.Transpile]
                public static int F() {
                    var xs = new List<int>();
                    xs.Add(1);
                    xs.Add(2);
                    return xs.Count;
                }
            }
            """);
        Assert.Contains("xs.push(1);", ts);
        Assert.Contains("xs.push(2);", ts);
        Assert.Contains("return xs.length;", ts);
    }

    [Fact]
    public void ListContains_Emits_Includes()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Collections.Generic;
            public static class S {
                [Mirrorgen.Transpile]
                public static bool F(List<int> xs, int n) => xs.Contains(n);
            }
            """);
        Assert.Contains("return xs.includes(n);", ts);
    }

    [Fact]
    public void Array_Length_Emits_Length()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static int F(int[] arr) => arr.Length;
            }
            """);
        Assert.Contains("return arr.length;", ts);
    }

    [Fact]
    public void Dictionary_Count_Emits_Object_Keys_Length()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Collections.Generic;
            public static class S {
                [Mirrorgen.Transpile]
                public static int F(Dictionary<string, int> m) => m.Count;
            }
            """);
        Assert.Contains("return Object.keys(m).length;", ts);
    }

    [Fact]
    public void Unsupported_New_Throws()
    {
        Assert.Throws<System.NotSupportedException>(() =>
            TranspilerEngine.TranspileSource("""
                public static class S {
                    [Mirrorgen.Transpile]
                    public static object F() => new object();
                }
                """));
    }

    [Fact]
    public void ListToArray_Emits_Slice()
    {
        // Both sides are `T[]` in TS, so only the copy needs reproducing.
        var ts = TranspilerEngine.TranspileSource("""
            using System.Collections.Generic;
            public static class S {
                [Mirrorgen.Transpile]
                public static int[] F(int n) {
                    var xs = new List<int>();
                    for (int i = 0; i < n; i++) xs.Add(i);
                    return xs.ToArray();
                }
            }
            """);
        Assert.Contains("return xs.slice();", ts);
    }
}
