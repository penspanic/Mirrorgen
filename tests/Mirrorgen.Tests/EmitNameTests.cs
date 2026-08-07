using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class EmitNameTests
{
    [Fact]
    public void EmitName_Renames_The_Exported_Function()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            public static class S
            {
                [Transpile(EmitName = "isWithinDistance")]
                public static bool IsWithinDistance(int x, int radius) => x < radius;
            }
            """);
        Assert.Contains("export function isWithinDistance(", ts);
        Assert.DoesNotContain("export function IsWithinDistance(", ts);
    }

    [Fact]
    public void EmitName_Omitted_Falls_Back_To_Csharp_Identifier()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            public static class S
            {
                [Transpile]
                public static int Same() => 1;
            }
            """);
        Assert.Contains("export function Same(", ts);
    }

    [Fact]
    public void EmitName_Empty_String_Treated_As_Unset()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            public static class S
            {
                [Transpile(EmitName = "")]
                public static int Same() => 1;
            }
            """);
        Assert.Contains("export function Same(", ts);
    }
    [Fact]
    public void EmitName_Rewrites_Same_Class_Call_Sites()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            [Transpile]
            public static class S
            {
                [Transpile(EmitName = "ProjectCellAttention")]
                public static int Project(int x) => x + 1;

                public static int Caller(int x) => Project(x) * 2;
            }
            """);
        Assert.Contains("export function ProjectCellAttention(", ts);
        // The call site must follow the renamed declaration — emitting a call
        // to the original C# name would reference an undefined function.
        Assert.Contains("Math.imul(ProjectCellAttention(x), 2)", ts);
        Assert.DoesNotContain("Math.imul(Project(x), 2)", ts);
    }

    [Fact]
    public void EmitName_Rewrites_Cross_Class_Call_Sites()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            [Transpile]
            public static class A
            {
                [Transpile(EmitName = "SampleUnique")]
                public static int Sample(int x) => x;
            }

            [Transpile]
            public static class B
            {
                public static int Use(int x) => A.Sample(x);
            }
            """);
        Assert.Contains("export function SampleUnique(", ts);
        Assert.Contains("SampleUnique(x)", ts);
    }

    [Fact]
    public void EmitName_On_A_Class_Static_Field_Renames_Declaration_And_Call_Sites()
    {
        // EmitName has to reach both the declaration and every call site, or a
        // renamed static becomes unreachable from the TS side.
        var ts = TranspilerEngine.TranspileSource("""
            using Mirrorgen;

            namespace X;

            [Transpile]
            public sealed class Proj
            {
                [Transpile(EmitName = "ProjInstance")]
                public static readonly Proj Instance = new();
                public int Scale(int x) => x * 2;
            }

            public static class Caller
            {
                [Transpile]
                public static int Use(int x) => Proj.Instance.Scale(x);
            }
            """);

        Assert.Contains("static ProjInstance: Proj = new Proj();", ts);
        Assert.DoesNotContain("static Instance:", ts);
        // Call site resolves to the renamed member, not the C# name.
        Assert.Contains("Proj.ProjInstance.Scale(x)", ts);
    }
}
