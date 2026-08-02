using System;
using System.IO;
using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

// Build-time path: TranspileFilesToWgsl folds cross-file consts against one
// shared compilation (real declarations, not a stub) and emits only the
// type-scoped subset, leaving TypeScript-only [Transpile] types alone.
public class WgslBatchTests : IDisposable
{
    readonly string _dir;

    public WgslBatchTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mirrorgen-wgsl-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Folds_Const_From_Another_File_And_Scopes_To_Named_Type()
    {
        // The const lives in a non-[Transpile] type in a separate file; the
        // model dispatches on it. Folding must resolve the real value (1).
        var enc = Write("Enc.cs", """
            namespace N;
            public static class Enc { public const byte BedrockTileId = 1; }
            """);
        // A TypeScript-only [Transpile] type the WGSL backend can't render
        // (expression-bodied) — it must be excluded by the type filter, not
        // throw.
        var export = Write("Export.cs", """
            namespace N;
            [Mirrorgen.Attributes.Transpile]
            public static class Export {
                public static byte Sand(byte g) => (byte)(2 + g);
            }
            """);
        var model = Write("Model.cs", """
            namespace N;
            [Mirrorgen.Attributes.Transpile]
            public static class Model {
                public static int Pick(int tile) {
                    if (tile == Enc.BedrockTileId) { return 7; }
                    return 0;
                }
            }
            """);

        var wgsl = TranspilerEngine.TranspileFilesToWgsl(
            new[] { enc, export, model }, new[] { "Model" });

        Assert.Contains("fn Pick(tile: i32) -> i32 {", wgsl);
        Assert.Contains("if (tile == i32(1u)) {", wgsl);  // folded cross-file const
        Assert.DoesNotContain("Enc.BedrockTileId", wgsl);
        Assert.DoesNotContain("fn Sand", wgsl);            // export type excluded by filter
    }

    [Fact]
    public void Shared_Tuple_Struct_Declared_Once_Across_Files()
    {
        var a = Write("A.cs", """
            namespace N;
            [Mirrorgen.Attributes.Transpile]
            public static class A {
                public static (byte R, byte G, byte B) Black() { return (0, 0, 0); }
            }
            """);
        var b = Write("B.cs", """
            namespace N;
            [Mirrorgen.Attributes.Transpile]
            public static class B {
                public static (byte R, byte G, byte B) White() { return (255, 255, 255); }
            }
            """);

        var wgsl = TranspilerEngine.TranspileFilesToWgsl(new[] { a, b }, new[] { "A", "B" });

        Assert.Contains("fn Black()", wgsl);
        Assert.Contains("fn White()", wgsl);
        // One shared struct preamble, not one per file.
        var first = wgsl.IndexOf("struct MgTuple_RGB", StringComparison.Ordinal);
        var last = wgsl.LastIndexOf("struct MgTuple_RGB", StringComparison.Ordinal);
        Assert.True(first >= 0 && first == last, "MgTuple_RGB must be declared exactly once");
    }

    // ── The type filter has to say when it selected nothing ──────────────────
    //
    // Both of these come from one real failure: a project asked for
    // "SandCanvas.Sim.SandCanvasWeatherField", got a syntactically valid WGSL
    // file with no functions in it, and a green build. The filter compared
    // against the bare identifier, so the qualified name matched nothing — and
    // matching nothing was indistinguishable from having nothing to emit.

    [Fact]
    public void Namespace_Qualified_Name_Selects_The_Type()
    {
        var src = Write("Field.cs", """
            namespace N.Deep;
            [Mirrorgen.Attributes.Transpile]
            public static class Field {
                public static int Twice(int x) { return x * 2; }
            }
            """);

        var wgsl = TranspilerEngine.TranspileFilesToWgsl(
            new[] { src }, new[] { "N.Deep.Field" });

        Assert.Contains("fn Twice", wgsl);
    }

    [Fact]
    public void Bare_Name_Still_Selects_The_Type()
    {
        var src = Write("Field.cs", """
            namespace N.Deep;
            [Mirrorgen.Attributes.Transpile]
            public static class Field {
                public static int Twice(int x) { return x * 2; }
            }
            """);

        var wgsl = TranspilerEngine.TranspileFilesToWgsl(
            new[] { src }, new[] { "Field" });

        Assert.Contains("fn Twice", wgsl);
    }

    [Fact]
    public void Type_Filter_Matching_Nothing_Is_An_Error()
    {
        var src = Write("Field.cs", """
            namespace N.Deep;
            [Mirrorgen.Attributes.Transpile]
            public static class Field {
                public static int Twice(int x) { return x * 2; }
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(
            () => TranspilerEngine.TranspileFilesToWgsl(new[] { src }, new[] { "Typo" }));

        // The message has to name what was asked for; "no output" on its own is
        // what made this take a day to find.
        Assert.Contains("Typo", ex.Message);
    }

    [Fact]
    public void Partially_Matching_Filter_Reports_Only_The_Miss()
    {
        var src = Write("Field.cs", """
            namespace N.Deep;
            [Mirrorgen.Attributes.Transpile]
            public static class Field {
                public static int Twice(int x) { return x * 2; }
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(
            () => TranspilerEngine.TranspileFilesToWgsl(new[] { src }, new[] { "Field", "Typo" }));

        Assert.Contains("Typo", ex.Message);
        Assert.DoesNotContain("'Field'", ex.Message);
    }

    /// <summary>
    /// A const declared in the same type as the method using it must fold, the
    /// way a cross-file one does. WGSL has no notion of a C# const, so leaving
    /// the identifier in place emits a file that references an undeclared
    /// symbol — valid-looking text that no shader can compile. TypeScript
    /// folds these already; the two backends disagreed.
    /// </summary>
    [Fact]
    public void Folds_Private_Const_Declared_In_The_Same_Type()
    {
        var src = Write("F.cs", """
            namespace N;
            [Mirrorgen.Attributes.Transpile]
            public static class F {
                private const int Bits = 8;
                private const int Mask = (1 << Bits) - 1;
                public static int Mix(int x) { return (x >> Bits) + (x & Mask); }
            }
            """);

        var wgsl = TranspilerEngine.TranspileFilesToWgsl(new[] { src }, new[] { "F" });

        Assert.Contains("x >> 8", wgsl);
        Assert.Contains("x & 255", wgsl);
        Assert.DoesNotContain("Bits", wgsl);
        Assert.DoesNotContain("Mask", wgsl);
    }

    /// <summary>
    /// A type whose every method is an instance method emits nothing, and that
    /// is legal — WGSL has no receiver — but it is not the same as a filter
    /// that matched nothing, and the two must not report the same way.
    /// </summary>
    [Fact]
    public void Matched_Type_With_No_Static_Methods_Is_Not_An_Error()
    {
        var src = Write("Inst.cs", """
            namespace N;
            [Mirrorgen.Attributes.Transpile]
            public sealed record Inst(int Seed) {
                public int Twice(int x) { return x * Seed; }
            }
            """);

        var wgsl = TranspilerEngine.TranspileFilesToWgsl(new[] { src }, new[] { "Inst" });

        Assert.DoesNotContain("fn Twice", wgsl);
    }
}
