using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class DirectoryMarkerScanTests : IDisposable
{
    readonly string _root;

    public DirectoryMarkerScanTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mirrorgen-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Marker_Match_Treats_Public_Types_As_Transpile_Targets()
    {
        var src = Path.Combine(_root, "src");
        var sharedData = Path.Combine(src, "Shared", "Data");
        Directory.CreateDirectory(sharedData);
        File.WriteAllText(Path.Combine(sharedData, "Foo.cs"), """
            public record Foo(int X, string Name);
            """);

        var outDir = Path.Combine(_root, "out");
        var opts = new TranspileOptions
        {
            ScanPathMarkers = new[] { Path.Combine("Shared", "Data") }
        };
        BatchTranspiler.TranspileDirectory(src, outDir, opts);

        var fooPath = Path.Combine(outDir, "Shared", "Data", "Foo.ts");
        Assert.True(File.Exists(fooPath), $"Expected emitted {fooPath}");
        var ts = File.ReadAllText(fooPath);
        Assert.Contains("export interface Foo {", ts);
        Assert.Contains("X: number;", ts);
        Assert.Contains("Name: string;", ts);
    }

    [Fact]
    public void Marker_Mode_Does_Not_Emit_Methods()
    {
        // TsGen 'data' mode emits shape only — methods inside marker-matched
        // files should not be transpiled (use [Transpile] explicitly for that).
        var src = Path.Combine(_root, "src");
        var dataDir = Path.Combine(src, "Shared", "Data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "K.cs"), """
            public static class K {
                public const int Magic = 42;
                public static int F(int x) => x + 1;
            }
            """);

        var outDir = Path.Combine(_root, "out");
        var opts = new TranspileOptions
        {
            ScanPathMarkers = new[] { Path.Combine("Shared", "Data") }
        };
        BatchTranspiler.TranspileDirectory(src, outDir, opts);

        var kPath = Path.Combine(outDir, "Shared", "Data", "K.ts");
        Assert.True(File.Exists(kPath));
        var ts = File.ReadAllText(kPath);
        Assert.Contains("export const Magic: number = 42;", ts);
        Assert.DoesNotContain("export function F(", ts);
    }

    [Fact]
    public void Files_Outside_Marker_Path_Are_Not_Auto_Emitted()
    {
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(src, "Shared", "Data"));
        Directory.CreateDirectory(Path.Combine(src, "OtherDir"));
        File.WriteAllText(Path.Combine(src, "Shared", "Data", "Inside.cs"), """
            public record Inside(int A);
            """);
        File.WriteAllText(Path.Combine(src, "OtherDir", "Outside.cs"), """
            public record Outside(int B);
            """);

        var outDir = Path.Combine(_root, "out");
        var opts = new TranspileOptions
        {
            ScanPathMarkers = new[] { Path.Combine("Shared", "Data") }
        };
        BatchTranspiler.TranspileDirectory(src, outDir, opts);

        Assert.True(File.Exists(Path.Combine(outDir, "Shared", "Data", "Inside.ts")));
        Assert.False(File.Exists(Path.Combine(outDir, "OtherDir", "Outside.ts")));
    }

    [Fact]
    public void Marker_And_Attribute_Coexist_In_Same_Source_Tree()
    {
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(src, "Shared", "Data"));
        Directory.CreateDirectory(Path.Combine(src, "Code"));
        File.WriteAllText(Path.Combine(src, "Shared", "Data", "Foo.cs"), """
            public record Foo(int A);
            """);
        File.WriteAllText(Path.Combine(src, "Code", "Manual.cs"), """
            [Mirrorgen.Transpile]
            public record Manual(int B);
            """);

        var outDir = Path.Combine(_root, "out");
        var opts = new TranspileOptions
        {
            ScanPathMarkers = new[] { Path.Combine("Shared", "Data") }
        };
        BatchTranspiler.TranspileDirectory(src, outDir, opts);

        Assert.True(File.Exists(Path.Combine(outDir, "Shared", "Data", "Foo.ts")));
        Assert.True(File.Exists(Path.Combine(outDir, "Code", "Manual.ts")));
    }
}
