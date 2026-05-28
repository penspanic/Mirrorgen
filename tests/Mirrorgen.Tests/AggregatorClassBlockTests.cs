using System.IO;
using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class AggregatorClassBlockTests
{
    [Fact]
    public void Aggregated_Class_Body_Survives_Internal_Blank_Lines()
    {
        var src = """
            [Mirrorgen.Transpile]
            public class Foo
            {
                public int X { get; }
                public Foo(int x) { X = x; }
                public int Double() => X * 2;
            }
            """;
        var tmpRoot = Path.Combine(Path.GetTempPath(), $"mg-agg-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(tmpRoot);
        var srcFile = Path.Combine(tmpRoot, "Foo.cs");
        File.WriteAllText(srcFile, src);
        var outDir = Path.Combine(tmpRoot, "out");
        var options = new TranspileOptions { AggregateOutputFile = "bundle.ts" };
        try
        {
            BatchTranspiler.TranspileFiles(new[] { srcFile }, tmpRoot, outDir, options);
            var bundle = File.ReadAllText(Path.Combine(outDir, "bundle.ts"));
            Assert.Contains("export class Foo {", bundle);
            Assert.Contains("constructor(x: number)", bundle);
            Assert.Contains("Double(): number {", bundle);
            // The closing brace of the class must be present — earlier the
            // aggregator chopped the body at the first internal blank line.
            Assert.Contains("this.X = x;", bundle);
        }
        finally
        {
            Directory.Delete(tmpRoot, recursive: true);
        }
    }
}
