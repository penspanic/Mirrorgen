using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class InstanceClassStaticFoldTests
{
    [Fact]
    public void Multiple_Private_Static_Fields_All_Survive_Aggregation()
    {
        // Pre-fix the EmitInstanceClass static-fold packed every `const X = …;`
        // into a single block (no blank-line separators), then the aggregator's
        // block-splitter folded the whole block under the header's name —
        // dropping every const after the first. Each declaration now gets its
        // own blank-line-bounded block.
        var src = """
            using System;
            using Mirrorgen;
            [Transpile]
            public readonly record struct V(double X, double Y, double Z);
            [Transpile]
            public sealed class Holder {
                private static readonly V Alpha = new(1d, 0d, 0d);
                private static readonly V Beta = new(0d, 1d, 0d);
                private static readonly V Gamma = new(0d, 0d, 1d);
                public string Name => "holder";
            }
            """;
        var ts = TranspilerEngine.TranspileSource(src);
        Assert.Contains("const Alpha: V = { X: 1, Y: 0, Z: 0 };", ts);
        Assert.Contains("const Beta: V = { X: 0, Y: 1, Z: 0 };", ts);
        Assert.Contains("const Gamma: V = { X: 0, Y: 0, Z: 1 };", ts);
    }

    [Fact]
    public void Instance_Singleton_Emits_After_Class_Definition()
    {
        // `export const Instance = new T();` has to land *after* `export class T`
        // — TS rejects use-before-declaration on the class identifier even though
        // JS hoists class definitions at runtime.
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
        var classIdx = ts.IndexOf("export class Proj");
        var instanceIdx = ts.IndexOf("export const Instance: Proj = new Proj()");
        Assert.True(classIdx >= 0, "class declaration missing");
        Assert.True(instanceIdx >= 0, "Instance declaration missing");
        Assert.True(instanceIdx > classIdx,
            $"Instance must follow class declaration (class @ {classIdx}, instance @ {instanceIdx})");
    }
}
