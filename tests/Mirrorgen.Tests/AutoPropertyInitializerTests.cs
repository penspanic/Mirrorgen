using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class AutoPropertyInitializerTests
{
    [Fact]
    public void Instance_Auto_Property_With_Record_Initializer_Lowers_To_TS_Field_Assignment()
    {
        // `public Props Properties { get; } = new(true, 2.0);` —
        // observable on every reader of the class, so the initializer
        // has to round-trip to the JS class body.
        var src = """
            using System;
            using Mirrorgen;
            [Transpile]
            public readonly record struct Props(bool Active, double Aspect);
            [Transpile]
            public sealed class Holder {
                public Props Properties { get; } = new(true, 2.0);
                public string Name => "holder";
            }
            """;
        var ts = TranspilerEngine.TranspileSource(src);
        Assert.Contains("readonly Properties: Props = { Active: true, Aspect: 2 };", ts);
    }

    [Fact]
    public void Auto_Property_Without_Initializer_Still_Emits_Bare_Declaration()
    {
        // Regression for the pre-existing path — auto-property without
        // an initializer is still emitted as the bare `readonly X: T;`
        // because the type's positional ctor / record fold supplies the
        // value at instantiation time.
        var src = """
            using System;
            using Mirrorgen;
            [Transpile]
            public sealed class Holder {
                public int Count { get; }
                public string Name => "holder";
            }
            """;
        var ts = TranspilerEngine.TranspileSource(src);
        Assert.Contains("readonly Count: number;", ts);
        Assert.DoesNotContain("Count: number =", ts);
    }
}
