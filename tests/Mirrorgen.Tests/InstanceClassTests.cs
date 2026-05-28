using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

// Instance class emit — the walker auto-detects a class declaration with
// instance behavior (explicit ctor body, instance method, or expression-bodied
// property) and emits a TS class. Pure data-shape classes (auto-properties or
// fields only) keep the legacy interface emit. Records and structs always
// emit as interfaces.
public class InstanceClassTests
{
    [Fact]
    public void Empty_Class_Emits_Empty_Interface()
    {
        // No instance behavior → auto-detect lands on the data-shape path.
        // Empty class has no members to differentiate it from an interface
        // anyway, so the interface form is the right emit.
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public class Empty { }
            """);
        Assert.Contains("export interface Empty {", ts);
    }

    [Fact]
    public void AutoProperty_Becomes_Readonly_Field()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
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
            [Mirrorgen.Transpile]
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
            [Mirrorgen.Transpile]
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
            [Mirrorgen.Transpile]
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
            [Mirrorgen.Transpile]
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
            [Mirrorgen.Transpile]
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
            [Mirrorgen.Transpile]
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
    public void Data_Only_Class_Still_Emits_Interface()
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

            [Mirrorgen.Transpile]
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
            [Mirrorgen.Transpile]
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
    public void NoTranspile_Method_Is_Skipped()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public class Surface
            {
                public int X { get; }
                public Surface(int x) { X = x; }

                public int Visible() => X;

                [Mirrorgen.NoTranspile]
                public int Hidden() => X * 2;
            }
            """);
        Assert.Contains("Visible(): number {", ts);
        Assert.DoesNotContain("Hidden", ts);
    }

    [Fact]
    public void Out_Param_In_Instance_Method_Emits_Tuple_Return()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
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

            [Mirrorgen.Transpile]
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

    [Fact]
    public void Record_Class_With_Instance_Method_Emits_As_TS_Class()
    {
        // Record CLASS (not record struct) with an instance method or
        // computed property becomes a TS class — positional params land as
        // readonly auto-properties, the ctor body is synthesized, and the
        // instance method emits next to them.
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public sealed record Params(double Radius, int Level)
            {
                public int CellCount => Level * Level;
                public void Validate()
                {
                    if (Radius <= 0) throw new System.ArgumentOutOfRangeException(nameof(Radius));
                }
            }
            """);
        Assert.Contains("export class Params {", ts);
        Assert.Contains("readonly Radius: number;", ts);
        Assert.Contains("readonly Level: number;", ts);
        Assert.Contains("constructor(Radius: number, Level: number)", ts);
        Assert.Contains("this.Radius = Radius;", ts);
        Assert.Contains("this.Level = Level;", ts);
        Assert.Contains("get CellCount(): number", ts);
        Assert.Contains("Validate()", ts);
    }

    [Fact]
    public void Record_Struct_Equals_Emits_FieldWise_Comparison()
    {
        // C# auto-generates value equality on record struct; TS interface
        // emit has no structural compare, so `a.Equals(b)` and `a == b`
        // must expand into a field-by-field `===` join. Otherwise the
        // emitted `a === b` is reference equality and never matches for
        // two BigInt-bearing object literals.
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public readonly record struct Cell(ulong High, ulong Low);

            public static class S {
                [Mirrorgen.Transpile]
                public static bool SameEq(Cell a, Cell b) => a.Equals(b);
                [Mirrorgen.Transpile]
                public static bool SameOp(Cell a, Cell b) => a == b;
                [Mirrorgen.Transpile]
                public static bool DiffOp(Cell a, Cell b) => a != b;
            }
            """);
        Assert.Contains("a.High === b.High && a.Low === b.Low", ts);
        Assert.Contains("a.High !== b.High || a.Low !== b.Low", ts);
    }

    [Fact]
    public void Bare_Record_Class_Stays_Interface()
    {
        // No instance behaviour → record class still emits as interface (the
        // common DTO case). The class-emit gate is structural, not [Transpile]-
        // wide.
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public sealed record Dto(int A, string B);
            """);
        Assert.Contains("export interface Dto", ts);
        Assert.DoesNotContain("export class Dto", ts);
    }
}
