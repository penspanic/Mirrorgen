using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class EnumTests
{
    [Fact]
    public void Enum_With_Implicit_Values_Numbered_From_Zero()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public enum E { A, B, C }
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static int Pick() { return 0; }
            }
            """);
        // E has no [Transpile], so it should NOT be emitted.
        Assert.DoesNotContain("export enum E", ts);
    }

    [Fact]
    public void Enum_With_Transpile_Attribute_Emits_Members()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public enum Color { Red, Green, Blue }
            """);
        Assert.Contains("export enum Color {", ts);
        Assert.Contains("  Red = 0,", ts);
        Assert.Contains("  Green = 1,", ts);
        Assert.Contains("  Blue = 2,", ts);
        Assert.Contains("}", ts);
    }

    [Fact]
    public void Enum_With_Explicit_Values_Are_Preserved()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public enum Status { Active = 10, Inactive = 20, Pending = 30 }
            """);
        Assert.Contains("Active = 10,", ts);
        Assert.Contains("Inactive = 20,", ts);
        Assert.Contains("Pending = 30,", ts);
    }

    [Fact]
    public void Enum_With_Implicit_Increment_After_Explicit()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public enum Tier { Bronze = 5, Silver, Gold }
            """);
        Assert.Contains("Bronze = 5,", ts);
        Assert.Contains("Silver = 6,", ts);
        Assert.Contains("Gold = 7,", ts);
    }

    [Fact]
    public void Enum_With_Negative_Explicit_Value()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public enum Sign { Negative = -1, Zero = 0, Positive = 1 }
            """);
        Assert.Contains("Negative = -1,", ts);
        Assert.Contains("Zero = 0,", ts);
        Assert.Contains("Positive = 1,", ts);
    }

    [Fact]
    public void Enum_EmitName_Renames_Output()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile(EmitName = "OrderState")]
            public enum CsOrderState { Open, Closed }
            """);
        Assert.Contains("export enum OrderState {", ts);
        Assert.DoesNotContain("CsOrderState", ts);
    }
}
