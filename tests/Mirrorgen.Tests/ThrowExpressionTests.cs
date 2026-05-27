using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class ThrowExpressionTests
{
    [Fact]
    public void Switch_Expression_Discard_Throw_Emits_Direct_Throw()
    {
        // `_ => throw new Foo(...)` inside a switch expression — emit a direct
        // `throw new Bar(...)` statement instead of wrapping in an IIFE. The
        // IIFE wrap would leave the surrounding `return` and the trailing
        // safety-net throw both flagged as unreachable by TS strict mode.
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            public enum K { A = 0, B = 1 }
            [Mirrorgen.Transpile]
            public static class S {
                public static int Map(K k) => k switch {
                    K.A => 1,
                    K.B => 2,
                    _ => throw new ArgumentOutOfRangeException(nameof(k)),
                };
            }
            """);
        // Direct throw — not wrapped in IIFE.
        Assert.Contains("throw new RangeError(", ts);
        Assert.DoesNotContain("(() => { throw new RangeError", ts);
        // Catch-all throw arm suppresses the trailing safety-net guard so the
        // emit has only one throw, not two.
        Assert.DoesNotContain("switch expression: no arm matched", ts);
    }

    [Fact]
    public void Throw_Expression_Outside_Catch_All_Wraps_In_IIFE()
    {
        // A conditional arm `K.A => throw ...` keeps the IIFE wrap because
        // the throw is gated; the safety-net guard still needs to fire if
        // none of the conditional arms match.
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            public enum K { A = 0, B = 1 }
            [Mirrorgen.Transpile]
            public static class S {
                public static int Map(K k) => k switch {
                    K.A => throw new InvalidOperationException("A is invalid"),
                    K.B => 2,
                };
            }
            """);
        Assert.Contains("throw new Error(\"A is invalid\")", ts);
        Assert.Contains("switch expression: no arm matched", ts);
    }
}
