using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class LocalsAndAssignmentTests
{
    static string Transpile(string body, string returnType = "int", string paramList = "int a, int b") =>
        TranspilerEngine.TranspileSource($$"""
            public static class S {
                [Mirrorgen.Attributes.Transpile]
                public static {{returnType}} F({{paramList}}) {
                    {{body}}
                }
            }
            """);

    [Fact]
    public void LocalDecl_Int_With_Initializer()
    {
        var ts = Transpile("""
            int x = 5;
            return x;
            """);
        Assert.Contains("let x: number = 5;", ts);
        Assert.Contains("return x;", ts);
    }

    [Fact]
    public void LocalDecl_Bool()
    {
        var ts = Transpile("bool b = true; return b;", returnType: "bool", paramList: "");
        Assert.Contains("let b: boolean = true;", ts);
    }

    [Fact]
    public void LocalDecl_String()
    {
        var ts = Transpile("""string s = "hi"; return s;""", returnType: "string", paramList: "");
        Assert.Contains("let s: string = \"hi\";", ts);
    }

    [Fact]
    public void LocalDecl_Var_Infers_Int()
    {
        var ts = Transpile("""
            var x = 5;
            return x;
            """);
        Assert.Contains("let x: number = 5;", ts);
    }

    [Fact]
    public void LocalDecl_With_Wrapped_Int_Arithmetic()
    {
        var ts = Transpile("""
            int dx = a - b;
            return dx;
            """);
        Assert.Contains("let dx: number = ((a - b) | 0);", ts);
    }

    [Fact]
    public void LocalDecl_Without_Initializer()
    {
        var ts = Transpile("""
            int x;
            return 0;
            """);
        Assert.Contains("let x: number;", ts);
    }

    [Fact]
    public void Simple_Assignment_To_Local()
    {
        var ts = Transpile("""
            int x = 0;
            x = 5;
            return x;
            """);
        Assert.Contains("x = 5;", ts);
    }

    [Fact]
    public void Compound_Assignment_Plus_Equals()
    {
        var ts = Transpile("""
            int x = 0;
            x += 3;
            return x;
            """);
        Assert.Contains("x += 3;", ts);
    }

    [Fact]
    public void Compound_Assignment_All_Forms()
    {
        var ts = Transpile("""
            int x = 10;
            x -= 1;
            x *= 2;
            x /= 3;
            x %= 4;
            return x;
            """);
        Assert.Contains("x -= 1;", ts);
        Assert.Contains("x *= 2;", ts);
        Assert.Contains("x /= 3;", ts);
        Assert.Contains("x %= 4;", ts);
    }
}
