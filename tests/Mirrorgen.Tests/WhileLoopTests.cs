using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class WhileLoopTests
{
    [Fact]
    public void While_Loop_With_Body()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static int Sum(int n) {
                    int i = 0;
                    int total = 0;
                    while (i < n) {
                        total += i;
                        i++;
                    }
                    return total;
                }
            }
            """);
        Assert.Contains("while (i < n) {", ts);
        Assert.Contains("    total = ((total + i) | 0);", ts);
        Assert.Contains("    i++;", ts);
    }

    [Fact]
    public void DoWhile_Loop_Emits_Do_While()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static int CountDown(int n) {
                    int i = n;
                    do {
                        i--;
                    } while (i > 0);
                    return i;
                }
            }
            """);
        Assert.Contains("do {", ts);
        Assert.Contains("    i--;", ts);
        Assert.Contains("} while (i > 0);", ts);
    }

    [Fact]
    public void Break_Statement_Inside_While()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static int FindFirst(int n) {
                    int i = 0;
                    while (i < 100) {
                        if (i == n) break;
                        i++;
                    }
                    return i;
                }
            }
            """);
        Assert.Contains("while (i < 100) {", ts);
        Assert.Contains("      break;", ts);
    }

    [Fact]
    public void Continue_Statement_Inside_While()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static int SkipEvens(int n) {
                    int i = 0;
                    int sum = 0;
                    while (i < n) {
                        i++;
                        if (i % 2 == 0) continue;
                        sum += i;
                    }
                    return sum;
                }
            }
            """);
        Assert.Contains("continue;", ts);
    }
}
