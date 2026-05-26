using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class MethodCallTests
{
    [Fact]
    public void Same_Class_Call_Emits_Bare_Name()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            public static class S {
                [Transpile]
                public static int Inc(int x) => Add(x, 1);

                [Transpile]
                public static int Add(int a, int b) => a + b;
            }
            """);
        Assert.Contains("export function Inc(x: number): number", ts);
        Assert.Contains("return Add(x, 1);", ts);
    }

    [Fact]
    public void Cross_Class_Call_Emits_Bare_Name()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            public static class Caller {
                [Transpile]
                public static int Run(int x) => Helpers.Double(x);
            }

            public static class Helpers {
                [Transpile]
                public static int Double(int v) => v * 2;
            }
            """);
        Assert.Contains("return Double(x);", ts);
    }

    [Fact]
    public void Call_Without_Args_Works()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            public static class S {
                [Transpile]
                public static int Wrap() => Inner();

                [Transpile]
                public static int Inner() => 42;
            }
            """);
        Assert.Contains("return Inner();", ts);
    }

    [Fact]
    public void Call_To_NonTranspile_Method_Throws()
    {
        Assert.Throws<NotSupportedException>(() =>
            TranspilerEngine.TranspileSource("""
                using Mirrorgen;

                public static class S {
                    [Transpile]
                    public static int Run(int x) => Helper(x);

                    public static int Helper(int x) => x;
                }
                """));
    }

    [Fact]
    public void Nested_Calls_Emit_Correctly()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            public static class S {
                [Transpile]
                public static int F(int x) => Add(Inc(x), Inc(x));

                [Transpile]
                public static int Inc(int x) => x + 1;

                [Transpile]
                public static int Add(int a, int b) => a + b;
            }
            """);
        Assert.Contains("return Add(Inc(x), Inc(x));", ts);
    }
}
