using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class SpecialTypeMappingTests
{
    [Fact]
    public void Guid_DateTime_TimeSpan_Map_To_String()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public class Foo {
                public System.Guid Id { get; init; }
                public System.DateTime Created { get; init; }
                public System.DateTimeOffset Modified { get; init; }
                public System.TimeSpan Duration { get; init; }
            }
            """);
        Assert.Contains("Id: string;", ts);
        Assert.Contains("Created: string;", ts);
        Assert.Contains("Modified: string;", ts);
        Assert.Contains("Duration: string;", ts);
    }

    [Fact]
    public void Object_And_JsonElement_Map_To_Unknown()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public class Foo {
                public object Payload { get; init; }
                public System.Text.Json.JsonElement Extra { get; init; }
            }
            """);
        Assert.Contains("Payload: unknown;", ts);
        Assert.Contains("Extra: unknown;", ts);
    }

    [Fact]
    public void Decimal_Maps_To_Number()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public class Foo {
                public decimal Price { get; init; }
            }
            """);
        Assert.Contains("Price: number;", ts);
    }

    [Fact]
    public void Nullable_Guid_Emits_Optional_String_Or_Null()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public class Foo {
                public System.Guid? MaybeId { get; init; }
            }
            """);
        Assert.Contains("MaybeId: string | null;", ts);
    }
}
