using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Mirrorgen.Core;

public static class BatchTranspiler
{
    public sealed record Result(int WrittenCount, int SkippedCount);

    /// <summary>
    /// Transpiles each source file relative to <paramref name="sourceRoot"/> into a
    /// matching .ts file under <paramref name="outputDir"/>. Files whose translation
    /// is empty (no `[Transpile]` members) are skipped. All source files share
    /// one CSharpCompilation so cross-file reachability works — a method in
    /// one file referencing a record in another inlines the record.
    /// </summary>
    public static Result TranspileFiles(IEnumerable<string> sourceFiles, string sourceRoot, string outputDir)
        => TranspileFiles(sourceFiles, sourceRoot, outputDir, TypeMappingRegistry.Empty, TranspileOptions.Default);

    public static Result TranspileFiles(IEnumerable<string> sourceFiles, string sourceRoot, string outputDir, TypeMappingRegistry registry)
        => TranspileFiles(sourceFiles, sourceRoot, outputDir, registry, TranspileOptions.Default);

    public static Result TranspileFiles(IEnumerable<string> sourceFiles, string sourceRoot, string outputDir, TranspileOptions options)
        => TranspileFiles(sourceFiles, sourceRoot, outputDir, TypeMappingRegistry.Empty, options);

    public static Result TranspileFiles(IEnumerable<string> sourceFiles, string sourceRoot, string outputDir, TypeMappingRegistry registry, TranspileOptions options)
    {
        var src = Path.GetFullPath(sourceRoot);
        var dst = Path.GetFullPath(outputDir);

        var fileList = new List<(string path, string relative, SyntaxTree tree)>();
        foreach (var csFile in sourceFiles)
        {
            var fullCs = Path.GetFullPath(csFile);
            var rel = Path.GetRelativePath(src, fullCs);
            var source = File.ReadAllText(fullCs);
            var tree = CSharpSyntaxTree.ParseText(source, path: fullCs);
            fileList.Add((fullCs, rel, tree));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "MirrorgenBatch",
            syntaxTrees: fileList.ConvertAll(e => e.tree),
            references: TranspilerEngine.PublicTrustedReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        int written = 0;
        int skipped = 0;
        var aggregateBuffer = options.AggregateOutputFile is null ? null : new List<string>();
        foreach (var (path, rel, tree) in fileList)
        {
            var ts = TranspilerEngine.TranspileTree(tree, compilation, registry, options);
            if (string.IsNullOrEmpty(ts))
            {
                skipped++;
                continue;
            }

            if (aggregateBuffer is not null)
            {
                aggregateBuffer.Add(ts);
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

        if (aggregateBuffer is not null && options.AggregateOutputFile is { } aggregateFile)
        {
            Directory.CreateDirectory(dst);
            var aggregated = AggregateOutputs(aggregateBuffer);
            var outFile = Path.Combine(dst, aggregateFile);
            File.WriteAllText(outFile, aggregated);
            written = aggregateBuffer.Count > 0 ? 1 : 0;
            // file-per-tree outputs already skipped via the early continue above
        }

        return new Result(written, skipped);
    }

    // Aggregated emit — folds N per-tree .ts outputs into a single string,
    // deduping top-level exports by name and merging helper-function
    // definitions (which Mirrorgen otherwise prepends to every tree that
    // references them) into a single occurrence at the top.
    // Top-level declarations to keep in the aggregated file. The optional
    // `export` makes module-local `const X = …;` (emitted for non-public
    // C# consts that body expressions still reference) survive the dedupe
    // pass. Without this they get silently dropped and the referencing TS
    // function ends up with an undefined-identifier error.
    static readonly Regex TopLevelExportRegex = new(
        @"^(?:export\s+)?(?:const|enum|function|interface|class)\s+(\w+)\b",
        RegexOptions.Compiled);
    static readonly Regex HelperFunctionRegex = new(
        @"^function\s+(__mirrorgen_\w+)\b",
        RegexOptions.Compiled);

    static string AggregateOutputs(List<string> outputs)
    {
        var helpers = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var exports = new Dictionary<string, string>(System.StringComparer.Ordinal);
        // Preserve the order in which each unique top-level entity was first
        // encountered so downstream tooling sees a stable layout.
        var helperOrder = new List<string>();
        var exportOrder = new List<string>();

        foreach (var text in outputs)
        {
            foreach (var block in SplitTopLevelBlocks(text))
            {
                var firstLine = block.TrimStart('\n', '\r', ' ');
                if (firstLine.Length == 0) continue;
                var newlineIdx = firstLine.IndexOf('\n');
                var header = newlineIdx >= 0 ? firstLine[..newlineIdx] : firstLine;
                var headerTrimmed = header.TrimEnd('\r', ' ');

                var helperMatch = HelperFunctionRegex.Match(headerTrimmed);
                if (helperMatch.Success)
                {
                    var name = helperMatch.Groups[1].Value;
                    if (!helpers.ContainsKey(name))
                    {
                        helpers[name] = block.TrimEnd('\n');
                        helperOrder.Add(name);
                    }
                    continue;
                }

                var exportMatch = TopLevelExportRegex.Match(headerTrimmed);
                if (exportMatch.Success)
                {
                    var name = exportMatch.Groups[1].Value;
                    if (!exports.ContainsKey(name))
                    {
                        exports[name] = block.TrimEnd('\n');
                        exportOrder.Add(name);
                    }
                    continue;
                }

                // Unrecognised block (e.g. a leading comment) — drop. Header
                // comments from individual emits are noise once aggregated.
            }
        }

        var sb = new StringBuilder();
        foreach (var name in helperOrder)
        {
            sb.AppendLine(helpers[name]);
            sb.AppendLine();
        }
        for (int i = 0; i < exportOrder.Count; i++)
        {
            sb.Append(exports[exportOrder[i]]);
            sb.AppendLine();
            if (i + 1 < exportOrder.Count) sb.AppendLine();
        }
        return sb.ToString();
    }

    // Splits a Mirrorgen-emitted .ts into top-level blocks separated by blank
    // lines. Multi-line declarations (interfaces / functions) stay intact
    // because their inner lines are non-empty.
    static IEnumerable<string> SplitTopLevelBlocks(string text)
    {
        var sb = new StringBuilder();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                continue;
            }
            sb.Append(line).Append('\n');
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    public static Result TranspileDirectory(string sourceDir, string outputDir)
        => TranspileDirectory(sourceDir, outputDir, TranspileOptions.Default);

    public static Result TranspileDirectory(string sourceDir, string outputDir, TranspileOptions options)
    {
        if (!Directory.Exists(sourceDir))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        }

        var src = Path.GetFullPath(sourceDir);
        var files = new List<string>();
        foreach (var csFile in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            // Skip .NET build artefact directories — those are generated, never user input.
            var rel = Path.GetRelativePath(src, csFile);
            var firstSegment = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (firstSegment is "bin" or "obj") continue;
            files.Add(csFile);
        }

        return TranspileFiles(files, src, outputDir, options);
    }
}
