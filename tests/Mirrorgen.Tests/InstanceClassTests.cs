using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

// Instance class emit (Shape = Class) — distinct from the default Shape = Interface
// path which folds everything into a data-only `export interface`. Class shape
// turns auto-properties into readonly fields, constructors into ctor bodies,
// instance methods into class methods, and expression-bodied get-only
// properties into getters.
public class InstanceClassTests
{
    [Fact]
    public void Empty_Class_Emits_Empty_Class()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile(Shape = Mirrorgen.TranspileShape.Class)]
            public class Empty { }
            """);
        Assert.Contains("export class Empty {", ts);
        Assert.DoesNotContain("export interface Empty", ts);
    }

    [Fact]
    public void AutoProperty_Becomes_Readonly_Field()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile(Shape = Mirrorgen.TranspileShape.Class)]
            public class Point
            {
                public int X { get; }
                public int Y { get; }

                public Point(int x, int y)
                {
                    X = x;
                    Y = y;
                }
            }
            """);
        Assert.Contains("export class Point {", ts);
        Assert.Contains("readonly X: number;", ts);
        Assert.Contains("readonly Y: number;", ts);
        Assert.Contains("constructor(x: number, y: number) {", ts);
        Assert.Contains("this.X = x;", ts);
        Assert.Contains("this.Y = y;", ts);
    }

    [Fact]
    public void Computed_Property_Becomes_Getter()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile(Shape = Mirrorgen.TranspileShape.Class)]
            public class Tag
            {
                public string Kind => "planar";
            }
            """);
        Assert.Contains("get Kind(): string {", ts);
        Assert.Contains("return \"planar\";", ts);
    }

    [Fact]
    public void Instance_Method_Emits_Class_Method_With_This_Binding()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile(Shape = Mirrorgen.TranspileShape.Class)]
            public class Counter
            {
                public int Value { get; }

                public Counter(int v) { Value = v; }

                public int Next() => Value + 1;
            }
            """);
        Assert.Contains("Next(): number {", ts);
        // Int return narrows via `| 0` per the existing walker convention —
        // we just need the `this.`-prefixed identifier to land somewhere
        // inside the method body.
        Assert.Contains("this.Value + 1", ts);
    }

    [Fact]
    public void Constructor_Validation_Throws_Emit()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            [Mirrorgen.Transpile(Shape = Mirrorgen.TranspileShape.Class)]
            public class Pos
            {
                public int X { get; }

                public Pos(int x)
                {
                    if (x < 0) throw new ArgumentOutOfRangeException(nameof(x));
                    X = x;
                }
            }
            """);
        Assert.Contains("constructor(x: number) {", ts);
        Assert.Contains("if (x < 0)", ts);
        Assert.Contains("throw new RangeError", ts);
    }

    [Fact]
    public void Default_Parameters_Emit_With_Equals_Default()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile(Shape = Mirrorgen.TranspileShape.Class)]
            public class Offset
            {
                public int X { get; }
                public int Y { get; }

                public Offset(int x = 0, int y = 0)
                {
                    X = x;
                    Y = y;
                }
            }
            """);
        Assert.Contains("constructor(x: number = 0, y: number = 0)", ts);
    }

    [Fact]
    public void Static_Method_On_Instance_Class_Emits_Static_Method()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile(Shape = Mirrorgen.TranspileShape.Class)]
            public class Foo
            {
                public int X { get; }

                public Foo(int x) { X = x; }

                public static Foo Default() => new Foo(0);
            }
            """);
        Assert.Contains("static Default(): Foo {", ts);
        Assert.Contains("return new Foo(0);", ts);
    }

    [Fact]
    public void Private_Field_Initialized_From_Constructor_Body()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile(Shape = Mirrorgen.TranspileShape.Class)]
            public class Half
            {
                public double Size { get; }
                private readonly double _half;

                public Half(double size)
                {
                    Size = size;
                    _half = size * 0.5;
                }
            }
            """);
        Assert.Contains("readonly Size: number;", ts);
        Assert.Contains("private readonly _half: number;", ts);
        Assert.Contains("this._half = size * 0.5;", ts);
    }

    [Fact]
    public void Default_Shape_Still_Emits_Interface()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public class LegacyDto
            {
                public int X { get; set; }
            }
            """);
        Assert.Contains("export interface LegacyDto {", ts);
        Assert.DoesNotContain("export class LegacyDto", ts);
    }

    [Fact]
    public void Implements_Transpiled_Interface()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public interface IThing
            {
                int Get();
            }

            [Mirrorgen.Transpile(Shape = Mirrorgen.TranspileShape.Class)]
            public class Thing : IThing
            {
                public int Value { get; }
                public Thing(int v) { Value = v; }
                public int Get() => Value;
            }
            """);
        Assert.Contains("export class Thing implements IThing {", ts);
    }

    [Fact]
    public void Iterator_Method_Emits_Generator_With_Yield()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System.Collections.Generic;
            [Mirrorgen.Transpile(Shape = Mirrorgen.TranspileShape.Class)]
            public class Range
            {
                public int Start { get; }
                public int End { get; }

                public Range(int start, int end) { Start = start; End = end; }

                public IEnumerable<int> Values()
                {
                    if (End < Start) yield break;
                    for (int i = Start; i < End; i++)
                        yield return i;
                }
            }
            """);
        Assert.Contains("*Values(): IterableIterator<number> {", ts);
        Assert.Contains("yield i;", ts);
        Assert.Contains("return;", ts);
    }

    [Fact]
    public void Out_Param_In_Instance_Method_Emits_Tuple_Return()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile(Shape = Mirrorgen.TranspileShape.Class)]
            public class Lookup
            {
                public int Value { get; }
                public Lookup(int v) { Value = v; }

                public bool TryGet(out int result)
                {
                    result = Value;
                    return true;
                }
            }
            """);
        // Out param surfaces in the return tuple along with the original
        // return type — matches the existing free-function ref/out convention.
        Assert.Contains("TryGet(result: number): [boolean, number] {", ts);
    }

    [Fact]
    public void Non_Transpiled_Base_Interface_Is_Dropped_From_Implements()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public interface IUnmirrored { }

            [Mirrorgen.Transpile(Shape = Mirrorgen.TranspileShape.Class)]
            public class Thing : IUnmirrored
            {
                public int X { get; }
                public Thing(int x) { X = x; }
            }
            """);
        // The implements clause should be omitted (not emit `implements IUnmirrored`).
        Assert.DoesNotContain("implements IUnmirrored", ts);
        Assert.Contains("export class Thing {", ts);
    }
}
