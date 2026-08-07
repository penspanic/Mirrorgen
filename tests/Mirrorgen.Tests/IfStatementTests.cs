using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class IfStatementTests
{
    static string Transpile(string body, string returnType = "int", string paramList = "int x") =>
        TranspilerEngine.TranspileSource($$"""
            public static class S {
                [Mirrorgen.Transpile]
                public static {{returnType}} F({{paramList}}) {
                    {{body}}
                }
            }
            """);

    [Fact]
    public void Bare_If_With_Block()
    {
        var ts = Transpile("""
            if (x > 0) {
                return 1;
            }
            return 0;
            """);
        Assert.Contains("if (x > 0) {", ts);
        Assert.Contains("    return 1;", ts);
        Assert.Contains("  }", ts);
        Assert.Contains("  return 0;", ts);
    }

    [Fact]
    public void If_Without_Braces()
    {
        var ts = Transpile("""
            if (x > 0) return 1;
            return 0;
            """);
        Assert.Contains("if (x > 0) {", ts);
        Assert.Contains("    return 1;", ts);
    }

    [Fact]
    public void If_Else()
    {
        var ts = Transpile("""
            if (x > 0) {
                return 1;
            } else {
                return -1;
            }
            """);
        Assert.Contains("if (x > 0) {", ts);
        Assert.Contains("} else {", ts);
        Assert.Contains("    return -1;", ts);
    }

    [Fact]
    public void Else_If_Chain()
    {
        var ts = Transpile("""
            if (x > 0) {
                return 1;
            } else if (x < 0) {
                return -1;
            } else {
                return 0;
            }
            """);
        // chain stays as `} else if (...)` rather than nested braces
        Assert.Contains("if (x > 0) {", ts);
        Assert.Contains("} else if (x < 0) {", ts);
        Assert.Contains("} else {", ts);
        Assert.Contains("    return 0;", ts);
    }

    [Fact]
    public void Nested_If_Indented_Two_Levels()
    {
        var ts = Transpile("""
            if (x > 0) {
                if (x > 10) {
                    return 2;
                }
                return 1;
            }
            return 0;
            """);
        Assert.Contains("  if (x > 0) {", ts);
        Assert.Contains("    if (x > 10) {", ts);
        Assert.Contains("      return 2;", ts);
        Assert.Contains("    return 1;", ts);
    }
}
