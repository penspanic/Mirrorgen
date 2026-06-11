using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class EmptyPropertyPatternTests
{
    [Fact]
    public void Is_EmptyPropertyPattern_Emits_NotNull_Check()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            [Transpile]
            public static class S
            {
                public static bool HasValue(string? s) => s is { };
            }
            """);
        Assert.Contains("s !== null", ts);
    }

    [Fact]
    public void IsNot_EmptyPropertyPattern_Emits_Null_Check()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            [Transpile]
            public static class S
            {
                public static bool Missing(string? s) => s is not { };
            }
            """);
        Assert.Contains("s === null", ts);
    }

    [Fact]
    public void EmptyPropertyPattern_With_Designation_Is_Rejected_With_Guidance()
    {
        var ex = Assert.Throws<System.NotSupportedException>(() =>
            TranspilerEngine.TranspileSource("""
                using Mirrorgen;

                [Transpile]
                public static class S
                {
                    public static int Len(string? s) => s is { } v ? v.Length : 0;
                }
                """));
        Assert.Contains("designation", ex.Message);
    }
}
