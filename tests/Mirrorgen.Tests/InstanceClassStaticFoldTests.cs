using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class InstanceClassStaticFoldTests
{
    [Fact]
    public void Multiple_Private_Static_Fields_All_Survive_Aggregation()
    {
        // Statics on a class-shape type are members of the emitted class, so
        // all three survive regardless of how the aggregator splits blocks —
        // they are not top-level declarations at all any more. (They used to
        // be module-scope consts, where a packed block made the aggregator drop
        // every one after the first.)
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
        Assert.Contains("static Alpha: V = { X: 1, Y: 0, Z: 0 };", ts);
        Assert.Contains("static Beta: V = { X: 0, Y: 1, Z: 0 };", ts);
        Assert.Contains("static Gamma: V = { X: 0, Y: 0, Z: 1 };", ts);
    }

    [Fact]
    public void Instance_Singleton_Is_A_Static_Member_Of_Its_Class()
    {
        // The singleton belongs to the class rather than to module scope, so
        // `Proj.Instance` namespaces itself. A static initializer that
        // references its own class is well-formed — the class binding is live
        // by the time static field initializers run.
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
        Assert.Contains("export class Proj {", ts);
        Assert.Contains("static Instance: Proj = new Proj();", ts);
        Assert.DoesNotContain("export const Instance", ts);
    }

    [Fact]
    public void Class_Statics_Are_Reachable_From_Inside_And_Outside()
    {
        // Moving statics into the class body means a bare reference from one of
        // the class's own methods has to be qualified too, or it resolves to
        // nothing.
        var src = """
            using Mirrorgen;
            namespace X;
            [Transpile]
            public sealed class Holder {
                private static readonly int[] Table = new int[] { 1, 2, 3 };
                public static readonly Holder Instance = new();
                public int Pick(int i) => Table[i];
            }
            public static class Uses {
                [Transpile] public static int Go(int i) => Holder.Instance.Pick(i);
            }
            """;
        var ts = TranspilerEngine.TranspileSource(src);
        Assert.Contains("static Table: number[] = [1, 2, 3];", ts);
        Assert.Contains("return Holder.Table[i]!;", ts);
        Assert.Contains("return Holder.Instance.Pick(i);", ts);
    }
}
