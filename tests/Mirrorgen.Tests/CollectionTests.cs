using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class CollectionTests
{
    [Fact]
    public void ListInt_Property_Emits_NumberArray()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Collections.Generic;

            [Mirrorgen.Transpile]
            public class Cart
            {
                public List<int> LineIds { get; set; } = new();
            }
            """);
        Assert.Contains("  LineIds: number[];", ts);
    }

    [Fact]
    public void IReadOnlyListString_Property_Emits_StringArray()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Collections.Generic;

            [Mirrorgen.Transpile]
            public record Order(IReadOnlyList<string> Skus);
            """);
        Assert.Contains("  Skus: string[];", ts);
    }

    [Fact]
    public void ListInt_Parameter_In_Transpile_Method()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Collections.Generic;

            public static class S
            {
                [Mirrorgen.Transpile]
                public static int First(List<int> xs) => xs[0];
            }
            """);
        Assert.Contains("First(xs: number[]): number", ts);
    }

    [Fact]
    public void ForEach_Over_ListInt_Emits_For_Of()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Collections.Generic;

            public static class S
            {
                [Mirrorgen.Transpile]
                public static int Sum(List<int> xs)
                {
                    int total = 0;
                    foreach (var x in xs) total += x;
                    return total;
                }
            }
            """);
        Assert.Contains("Sum(xs: number[])", ts);
        Assert.Contains("for (const x of xs) {", ts);
    }

    [Fact]
    public void DictionaryStringInt_Emits_Record_Of_StringNumber()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Collections.Generic;

            [Mirrorgen.Transpile]
            public record Inventory(Dictionary<string, int> Counts);
            """);
        Assert.Contains("  Counts: Record<string, number>;", ts);
    }

    [Fact]
    public void IReadOnlyDictionaryIntString_In_Method_Parameter()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Collections.Generic;

            public static class S
            {
                [Mirrorgen.Transpile]
                public static string Lookup(IReadOnlyDictionary<int, string> map, int key) => map[key];
            }
            """);
        Assert.Contains("Lookup(map: Record<number, string>, key: number): string", ts);
    }

    [Fact]
    public void HashSet_Field_Emits_As_TS_Set()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Collections.Generic;

            [Mirrorgen.Transpile]
            public record Bag(HashSet<int> Items);
            """);
        Assert.Contains("Items: Set<number>;", ts);
    }

    [Fact]
    public void Unsupported_Generic_Throws()
    {
        // Linked-list style generics still have no TS mirror.
        Assert.Throws<NotSupportedException>(() =>
            TranspilerEngine.TranspileSource("""
                using System.Collections.Generic;

                [Mirrorgen.Transpile]
                public record Bad(LinkedList<int> Items);
                """));
    }
}
