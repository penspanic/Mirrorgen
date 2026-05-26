using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class EmitNameTests
{
    [Fact]
    public void EmitName_Renames_The_Exported_Function()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            public static class S
            {
                [Transpile(EmitName = "isWithinDistance")]
                public static bool IsWithinDistance(int x, int radius) => x < radius;
            }
            """);
        Assert.Contains("export function isWithinDistance(", ts);
        Assert.DoesNotContain("export function IsWithinDistance(", ts);
    }

    [Fact]
    public void EmitName_Omitted_Falls_Back_To_Csharp_Identifier()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            public static class S
            {
                [Transpile]
                public static int Same() => 1;
            }
            """);
        Assert.Contains("export function Same(", ts);
    }

    [Fact]
    public void EmitName_Empty_String_Treated_As_Unset()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            public static class S
            {
                [Transpile(EmitName = "")]
                public static int Same() => 1;
            }
            """);
        Assert.Contains("export function Same(", ts);
    }
}
