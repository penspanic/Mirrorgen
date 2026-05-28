using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class InstanceSingletonTests
{
    [Fact]
    public void Class_Shape_StaticReadonly_Instance_Emits_New_Not_Empty_Object()
    {
        // Canonical singleton pattern (`static readonly T Instance = new();`).
        // Pre-fix this walked the target-typed `new()` through the
        // record-construction path and emitted as an empty object literal `{}` —
        // useless for a class whose whole point is its instance methods. The
        // class-shape branch now emits `new T()` so the singleton is callable.
        var src = """
            using System;
            using Mirrorgen;
            [Transpile]
            public sealed class Proj {
                public static readonly Proj Instance = new();
                public string Name => "proj";
            }
            """;
        var ts = TranspilerEngine.TranspileSource(src);
        Assert.Contains("Instance: Proj = new Proj()", ts);
        Assert.DoesNotContain("Instance: Proj = {  }", ts);
        Assert.DoesNotContain("Instance: Proj = {}", ts);
    }
}
