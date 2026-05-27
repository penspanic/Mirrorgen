using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class NumericsValueTypeTests
{
    [Fact]
    public void Vector3_Return_Type_Emits_As_Inline_Structural_Shape()
    {
        // System.Numerics.Vector3 doesn't get a separate TS interface — it
        // emits as the inline `{ X: number; Y: number; Z: number }` shape
        // wherever it appears, so callers don't need a runtime import.
        var ts = TranspilerEngine.TranspileSource("""
            using System.Numerics;
            [Mirrorgen.Transpile]
            public static class S {
                public static Vector3 Up() => new Vector3(0f, 1f, 0f);
            }
            """);
        Assert.Contains("export function Up(): { X: number; Y: number; Z: number }", ts);
        Assert.Contains("return { X: 0, Y: 1, Z: 0 };", ts);
    }

    [Fact]
    public void Vector3_Field_Access_Stays_PascalCase()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Numerics;
            [Mirrorgen.Transpile]
            public static class S {
                public static double XOf(Vector3 v) => v.X;
            }
            """);
        Assert.Contains("export function XOf(v: { X: number; Y: number; Z: number }): number", ts);
        Assert.Contains("return v.X;", ts);
    }

    [Fact]
    public void Quaternion_Has_W_Field()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Numerics;
            [Mirrorgen.Transpile]
            public static class S {
                public static Quaternion Identity() => new Quaternion(0f, 0f, 0f, 1f);
            }
            """);
        Assert.Contains("{ X: number; Y: number; Z: number; W: number }", ts);
        Assert.Contains("return { X: 0, Y: 0, Z: 0, W: 1 };", ts);
    }
}
