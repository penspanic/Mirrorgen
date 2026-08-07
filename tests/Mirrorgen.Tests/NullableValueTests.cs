using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class NullableValueTests
{
    static string Transpile(string body, string returnType, string paramList) =>
        TranspilerEngine.TranspileSource($$"""
            using System;
            public static class S {
                [Mirrorgen.Transpile]
                public static {{returnType}} F({{paramList}}) {
                    {{body}}
                }
            }
            """);

    [Fact]
    public void Nullable_Value_Of_Double_Becomes_PassThrough()
    {
        // C# Nullable<double> lowers to JS `double | null`. After a null check
        // the value is just the receiver — `.Value` would be undefined.
        var ts = Transpile("return x.Value;", returnType: "double", paramList: "double? x");
        Assert.Contains("return x;", ts);
        Assert.DoesNotContain(".Value", ts);
    }
}
