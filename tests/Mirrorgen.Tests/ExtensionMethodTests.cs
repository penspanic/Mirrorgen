using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class ExtensionMethodTests
{
    [Fact]
    public void Extension_Invocation_Reifies_Receiver_As_First_Arg()
    {
        // `face.Normal()` → `Normal(face)` — the `this` parameter becomes a
        // plain positional arg on the TS side, where extension-method dispatch
        // doesn't exist. The receiver expression is emitted as-is, args
        // follow.
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            public enum CubeFace : byte { PosX = 0, NegX = 1 }
            [Mirrorgen.Transpile]
            public static class FaceHelpers {
                public static int Normal(this CubeFace face) => (int)face;
            }
            [Mirrorgen.Transpile]
            public static class Caller {
                public static int Use(CubeFace face) => face.Normal();
            }
            """);
        Assert.Contains("export function Normal(face: CubeFace): number", ts);
        Assert.Contains("return Normal(face);", ts);
    }

    [Fact]
    public void Extension_Invocation_With_Extra_Args_Concatenates()
    {
        // `face.Combine(3, 5)` → `Combine(face, 3, 5)` — receiver first, then
        // the call-site args.
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            public enum CubeFace : byte { PosX = 0 }
            [Mirrorgen.Transpile]
            public static class FaceHelpers {
                public static int Combine(this CubeFace face, int a, int b) => (int)face + a + b;
            }
            [Mirrorgen.Transpile]
            public static class Caller {
                public static int Use(CubeFace face) => face.Combine(3, 5);
            }
            """);
        Assert.Contains("return Combine(face, 3, 5);", ts);
    }
}
