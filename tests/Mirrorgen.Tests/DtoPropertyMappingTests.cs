using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class DtoPropertyMappingTests
{
    [Fact]
    public void Nullable_Int_Property_Emits_Optional_Marker_And_Null_Union()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public class Foo {
                public int? Count { get; init; }
            }
            """);
        Assert.Contains("Count?: number | null;", ts);
    }

    [Fact]
    public void Nullable_String_Property_Emits_Optional_Marker_And_Null_Union()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public class Foo {
                public string? Name { get; init; }
            }
            """);
        Assert.Contains("Name?: string | null;", ts);
    }

    [Fact]
    public void Nullable_Positional_Record_Param_Emits_Optional()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public record Foo(int? Maybe, string Required);
            """);
        Assert.Contains("Maybe?: number | null;", ts);
        Assert.Contains("Required: string;", ts);
    }

    [Fact]
    public void List_And_IReadOnlyList_And_IEnumerable_All_Map_To_TS_Array()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public class Foo {
                public System.Collections.Generic.List<int> A { get; init; }
                public System.Collections.Generic.IReadOnlyList<int> B { get; init; }
                public System.Collections.Generic.IList<int> C { get; init; }
                public System.Collections.Generic.IEnumerable<int> D { get; init; }
                public System.Collections.Generic.ICollection<int> E { get; init; }
                public System.Collections.Generic.IReadOnlyCollection<int> F { get; init; }
            }
            """);
        Assert.Contains("A: number[];", ts);
        Assert.Contains("B: number[];", ts);
        Assert.Contains("C: number[];", ts);
        Assert.Contains("D: number[];", ts);
        Assert.Contains("E: number[];", ts);
        Assert.Contains("F: number[];", ts);
    }

    [Fact]
    public void Dictionary_Variants_Map_To_Record()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public class Foo {
                public System.Collections.Generic.Dictionary<string, int> A { get; init; }
                public System.Collections.Generic.IDictionary<string, int> B { get; init; }
                public System.Collections.Generic.IReadOnlyDictionary<string, int> C { get; init; }
            }
            """);
        Assert.Contains("A: Record<string, number>;", ts);
        Assert.Contains("B: Record<string, number>;", ts);
        Assert.Contains("C: Record<string, number>;", ts);
    }

    [Fact]
    public void Byte_Array_Maps_To_String_For_Base64_Wire()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public class Foo {
                public byte[] Payload { get; init; }
            }
            """);
        Assert.Contains("Payload: string;", ts);
        Assert.DoesNotContain("Payload: number[];", ts);
    }
}
