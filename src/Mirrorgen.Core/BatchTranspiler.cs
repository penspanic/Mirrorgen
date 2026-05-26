using System.IO;

namespace Mirrorgen.Core;

public static class BatchTranspiler
{
    public sealed record Result(int WrittenCount, int SkippedCount);

    public static Result TranspileDirectory(string sourceDir, string outputDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        }

        var src = Path.GetFullPath(sourceDir);
        var dst = Path.GetFullPath(outputDir);

        int written = 0;
        int skipped = 0;

        foreach (var csFile in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            // Skip .NET build artefact directories — those are generated, never user input.
            var rel = Path.GetRelativePath(src, csFile);
            var firstSegment = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (firstSegment is "bin" or "obj") continue;

            var source = File.ReadAllText(csFile);
            var ts = TranspilerEngine.TranspileSource(source);
            if (string.IsNullOrEmpty(ts))
            {
                skipped++;
                continue;
            }

            var outFile = Path.Combine(dst, Path.ChangeExtension(rel, ".ts"));
            var outFileDir = Path.GetDirectoryName(outFile);
            if (!string.IsNullOrEmpty(outFileDir))
            {
                Directory.CreateDirectory(outFileDir);
            }
            File.WriteAllText(outFile, ts);
            written++;
        }

        return new Result(written, skipped);
    }
}
