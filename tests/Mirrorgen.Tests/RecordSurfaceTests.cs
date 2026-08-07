using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class RecordSurfaceTests
{
    [Fact]
    public void RecordStruct_Emits_As_Interface()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public record struct Point(int X, int Y);
            """);
        Assert.Contains("export interface Point {", ts);
        Assert.Contains("X: number;", ts);
        Assert.Contains("Y: number;", ts);
    }

    [Fact]
    public void Sealed_Record_Has_No_Special_Marker()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public sealed record Sealed(int X);
            """);
        Assert.Contains("export interface Sealed {", ts);
        Assert.Contains("X: number;", ts);
        Assert.DoesNotContain("sealed", ts);
        Assert.DoesNotContain("final", ts);
    }

    [Fact]
    public void Struct_Emits_As_Interface()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public struct Vec2 {
                public int X { get; init; }
                public int Y { get; init; }
            }
            """);
        Assert.Contains("export interface Vec2 {", ts);
        Assert.Contains("X: number;", ts);
    }

    [Fact]
    public void Record_Inheritance_Emits_Only_Own_Positional_Params()
    {
        // Matches TsGen behaviour — derived emits only its own params.
        // Base interface is emitted separately (when [Transpile]-marked).
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public record Base(int X);
            [Mirrorgen.Transpile]
            public record Derived(int Y) : Base(0);
            """);
        Assert.Contains("export interface Base {", ts);
        Assert.Contains("X: number;", ts);
        Assert.Contains("export interface Derived {", ts);

        // Derived interface must contain Y; finding "X: number;" twice would be wrong.
        var derivedIdx = ts.IndexOf("export interface Derived {");
        var derivedEnd = ts.IndexOf("}", derivedIdx);
        var derivedBody = ts.Substring(derivedIdx, derivedEnd - derivedIdx);
        Assert.Contains("Y: number;", derivedBody);
        Assert.DoesNotContain("X: number;", derivedBody);
    }

    [Fact]
    public void Abstract_Base_Record_Emits_Empty_Interface_For_Subtype_Resolution()
    {
        // Polymorphic base records like `abstract record TopologyParams;` carry
        // no own properties but downstream subtypes (`PlanarTopologyParams :
        // TopologyParams`) reference the base by name. The empty interface
        // keeps TS resolution working, matching TsGen's behaviour.
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public abstract record TopologyParams;
            [Mirrorgen.Transpile]
            public sealed record PlanarTopologyParams(double CellSize) : TopologyParams;
            """);
        Assert.Contains("export interface TopologyParams {", ts);
        Assert.Contains("export interface PlanarTopologyParams {", ts);
        Assert.Contains("CellSize: number;", ts);
    }

    [Fact]
    public void Partial_Class_Emits_Single_Combined_Interface()
    {
        // Two partial declarations in the same source — Roslyn merges them at
        // the symbol level. Mirrorgen should emit exactly one interface that
        // combines members from both halves.
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public partial class Foo {
                public int A { get; init; }
            }
            public partial class Foo {
                public int B { get; init; }
            }
            """);
        var firstIdx = ts.IndexOf("export interface Foo {");
        var lastIdx = ts.LastIndexOf("export interface Foo {");
        Assert.True(firstIdx >= 0, "Foo interface should appear");
        Assert.Equal(firstIdx, lastIdx); // exactly one interface declaration
        Assert.Contains("A: number;", ts);
        Assert.Contains("B: number;", ts);
    }
}
