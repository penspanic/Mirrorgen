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
}
