using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class BatchTranspilerTests : IDisposable
{
    readonly string _root;

    public BatchTranspilerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mirrorgen-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Mirrors_Directory_Structure()
    {
        var src = Path.Combine(_root, "src");
        var outDir = Path.Combine(_root, "out");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "a.cs"), """
            public static class A {
                [Mirrorgen.Transpile]
                public static int F() => 1;
            }
            """);
        File.WriteAllText(Path.Combine(src, "sub", "b.cs"), """
            public static class B {
                [Mirrorgen.Transpile]
                public static int G() => 2;
            }
            """);

        var result = BatchTranspiler.TranspileDirectory(src, outDir);

        Assert.Equal(2, result.WrittenCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.True(File.Exists(Path.Combine(outDir, "a.ts")));
        Assert.True(File.Exists(Path.Combine(outDir, "sub", "b.ts")));
        Assert.Contains("export function F()", File.ReadAllText(Path.Combine(outDir, "a.ts")));
        Assert.Contains("export function G()", File.ReadAllText(Path.Combine(outDir, "sub", "b.ts")));
    }

    [Fact]
    public void Skips_Files_With_No_Transpile_Members()
    {
        var src = Path.Combine(_root, "src");
        var outDir = Path.Combine(_root, "out");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "transpiled.cs"), """
            public static class T {
                [Mirrorgen.Transpile]
                public static int F() => 1;
            }
            """);
        File.WriteAllText(Path.Combine(src, "plain.cs"), """
            public static class P {
                public static int Hidden() => 99;
            }
            """);

        var result = BatchTranspiler.TranspileDirectory(src, outDir);

        Assert.Equal(1, result.WrittenCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.True(File.Exists(Path.Combine(outDir, "transpiled.ts")));
        Assert.False(File.Exists(Path.Combine(outDir, "plain.ts")));
    }

    [Fact]
    public void Skips_Bin_And_Obj_Directories()
    {
        var src = Path.Combine(_root, "src");
        var outDir = Path.Combine(_root, "out");
        Directory.CreateDirectory(Path.Combine(src, "obj", "Debug"));
        Directory.CreateDirectory(Path.Combine(src, "bin", "Release"));
        File.WriteAllText(Path.Combine(src, "real.cs"), """
            public static class R {
                [Mirrorgen.Transpile]
                public static int F() => 1;
            }
            """);
        File.WriteAllText(Path.Combine(src, "obj", "Debug", "Generated.cs"), """
            public static class G {
                [Mirrorgen.Transpile]
                public static int Leaked() => 99;
            }
            """);
        File.WriteAllText(Path.Combine(src, "bin", "Release", "Built.cs"), """
            public static class B {
                [Mirrorgen.Transpile]
                public static int AlsoLeaked() => 99;
            }
            """);

        var result = BatchTranspiler.TranspileDirectory(src, outDir);

        Assert.Equal(1, result.WrittenCount);
        Assert.True(File.Exists(Path.Combine(outDir, "real.ts")));
        Assert.False(Directory.Exists(Path.Combine(outDir, "obj")));
        Assert.False(Directory.Exists(Path.Combine(outDir, "bin")));
    }

    [Fact]
    public void Throws_When_Source_Directory_Missing()
    {
        var missing = Path.Combine(_root, "does-not-exist");
        Assert.Throws<DirectoryNotFoundException>(() =>
            BatchTranspiler.TranspileDirectory(missing, Path.Combine(_root, "out")));
    }
}
