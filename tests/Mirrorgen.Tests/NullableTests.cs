using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class NullableTests
{
    [Fact]
    public void Nullable_Int_Property_Emits_Number_Or_Null()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public class Reading
            {
                public int? Value { get; set; }
            }
            """);
        Assert.Contains("  Value?: number | null;", ts);
    }

    [Fact]
    public void Nullable_String_Property_Emits_String_Or_Null()
    {
        var ts = TranspilerEngine.TranspileSource("""
            #nullable enable
            [Mirrorgen.Attributes.Transpile]
            public class Profile
            {
                public string? Nickname { get; set; }
            }
            """);
        Assert.Contains("  Nickname?: string | null;", ts);
    }

    [Fact]
    public void Nullable_Int_In_Positional_Record()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public record Sample(int Id, int? OptionalScore);
            """);
        Assert.Contains("  Id: number;", ts);
        Assert.Contains("  OptionalScore?: number | null;", ts);
    }

    [Fact]
    public void Nullable_Int_Parameter_In_Transpile_Method()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S
            {
                [Mirrorgen.Attributes.Transpile]
                public static bool HasValue(int? v) => v != null;
            }
            """);
        Assert.Contains("HasValue(v: number | null)", ts);
    }
}
