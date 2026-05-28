using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class TupleDeconstructionTests
{
    [Fact]
    public void Named_Tuple_Decon_Becomes_Object_Destructure()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            public static class S {
                [Mirrorgen.Transpile]
                public static (int X, int Y) Pair() => (1, 2);

                [Mirrorgen.Transpile]
                public static int Sum() {
                    var (x, y) = Pair();
                    return x + y;
                }
            }
            """);
        Assert.Contains("let { X: x, Y: y } = Pair();", ts);
    }

    [Fact]
    public void Decon_With_Field_Eq_Local_Uses_Shorthand()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            public static class S {
                [Mirrorgen.Transpile]
                public static (int X, int Y) Pair() => (1, 2);

                [Mirrorgen.Transpile]
                public static int Sum() {
                    var (X, Y) = Pair();
                    return X + Y;
                }
            }
            """);
        Assert.Contains("let { X, Y } = Pair();", ts);
    }

    [Fact]
    public void Unnamed_Tuple_Decon_Falls_Back_To_Array_Destructure()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            public static class S {
                [Mirrorgen.Transpile]
                public static (int, int) Pair() => (1, 2);

                [Mirrorgen.Transpile]
                public static int Sum() {
                    var (x, y) = Pair();
                    return x + y;
                }
            }
            """);
        Assert.Contains("let [x, y] = Pair();", ts);
    }
}
