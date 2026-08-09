using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Mirrorgen.Core;

public static class BatchTranspiler
{
    /// <param name="WrittenCount">Files created or replaced on disk.</param>
    /// <param name="SkippedCount">Sources that transpiled to nothing — no
    /// <c>[Transpile]</c> members — so no file was ever going to exist.</param>
    /// <param name="UnchangedCount">Files whose content on disk already matched,
    /// so nothing was written. Distinct from <paramref name="SkippedCount"/>:
    /// this output exists and is current, it just did not need touching.</param>
    public sealed record Result(int WrittenCount, int SkippedCount, int UnchangedCount = 0);

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
        int unchanged = 0;
        var aggregateBuffer = options.AggregateOutputFile is null ? null : new List<string>();
        // Source path -> emitted .ts path, relative to the output directory.
        // Needed to turn a cross-file call into a module specifier.
        var relBySourcePath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, rel, _) in fileList)
        {
            relBySourcePath[path] = Path.ChangeExtension(rel, ".ts");
        }

        foreach (var (path, rel, tree) in fileList)
        {
            var ts = TranspilerEngine.TranspileTree(tree, compilation, registry, options, out var externals);
            if (string.IsNullOrEmpty(ts))
            {
                skipped++;
                continue;
            }

            if (aggregateBuffer is not null)
            {
                // One module: file boundaries disappear, so nothing to import.
                aggregateBuffer.Add(ts);
                continue;
            }

            if (externals.Count > 0)
            {
                ts = BuildImportHeader(externals, Path.ChangeExtension(rel, ".ts"), relBySourcePath, options.ImportExtension) + ts;
            }

            var outFile = Path.Combine(dst, Path.ChangeExtension(rel, ".ts"));
            if (GeneratedFile.Write(outFile, ts) == GeneratedFile.Outcome.Written)
                written++;
            else
                unchanged++;
        }

        if (aggregateBuffer is not null && options.AggregateOutputFile is { } aggregateFile)
        {
            var aggregated = AggregateOutputs(aggregateBuffer);
            var outFile = Path.Combine(dst, aggregateFile);
            if (aggregateBuffer.Count == 0)
            {
                written = 0;
            }
            else if (GeneratedFile.Write(outFile, aggregated) == GeneratedFile.Outcome.Written)
            {
                written = 1;
                unchanged = 0;
            }
            else
            {
                written = 0;
                unchanged = 1;
            }
            // file-per-tree outputs already skipped via the early continue above
        }

        return new Result(written, skipped, unchanged);
    }

    /// <summary>
    /// Turns the cross-file [Transpile] calls a tree makes into import
    /// statements. Without these the emitted module calls an identifier that
    /// was never declared or imported — the build stays green and the failure
    /// surfaces at run time.
    /// </summary>
    static string BuildImportHeader(
        IReadOnlyList<TranspilerEngine.ExternalMethodRef> externals,
        string selfRelativeTsPath,
        Dictionary<string, string> relBySourcePath,
        string importExtension)
    {
        var byModule = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var ext in externals)
        {
            if (!relBySourcePath.TryGetValue(ext.DeclaringFilePath, out var targetRelTs))
            {
                // Declared outside the set of files being transpiled — there is
                // no emitted module to point at, so leave it alone rather than
                // inventing a specifier.
                continue;
            }
            var specifier = ToModuleSpecifier(selfRelativeTsPath, targetRelTs, importExtension);
            if (!byModule.TryGetValue(specifier, out var names))
            {
                names = new SortedSet<string>(StringComparer.Ordinal);
                byModule[specifier] = names;
            }
            names.Add(ext.EmitName);
        }
        if (byModule.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var (specifier, names) in byModule)
        {
            sb.Append("import { ").Append(string.Join(", ", names))
              .Append(" } from '").Append(specifier).AppendLine("';");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// Relative module specifier from one emitted .ts to another, always
    /// explicitly relative ('./' or '../') so it is never mistaken for a
    /// package name.
    /// </summary>
    static string ToModuleSpecifier(string selfRelativeTsPath, string targetRelativeTsPath, string importExtension)
    {
        var selfDir = Path.GetDirectoryName(selfRelativeTsPath);
        var rel = string.IsNullOrEmpty(selfDir)
            ? targetRelativeTsPath
            : Path.GetRelativePath(selfDir, targetRelativeTsPath);
        rel = rel.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        if (rel.EndsWith(".ts", System.StringComparison.Ordinal))
        {
            rel = rel[..^3] + importExtension;
        }
        if (!rel.StartsWith("./", System.StringComparison.Ordinal) &&
            !rel.StartsWith("../", System.StringComparison.Ordinal))
        {
            rel = "./" + rel;
        }
        return rel;
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
                    var body = block.TrimEnd('\n');
                    if (exports.TryGetValue(name, out var existing))
                    {
                        // Two blocks can share a name for two very different
                        // reasons. Types are deliberately inlined into every
                        // tree that references them, so the *same* declaration
                        // legitimately shows up N times — identical text is
                        // that case, and dropping the extras is right.
                        //
                        // Different text means two *different* C# members
                        // collided on one TS identifier (e.g. a `static
                        // readonly T Instance` on each of two classes, both
                        // hoisted to module scope). TypeScript has a single
                        // module scope, so keeping the first silently deletes
                        // the second — the consumer gets a module that is
                        // quietly missing a declaration it asked for.
                        if (!string.Equals(existing, body, System.StringComparison.Ordinal))
                        {
                            throw new NotSupportedException(
                                $"Aggregated emit produced two different top-level declarations named '{name}'. "
                                + "TypeScript has one module scope, so only one of them can survive. "
                                + "Rename one of the C# members with [Transpile(EmitName = \"...\")].\n"
                                + $"  first:  {FirstLine(existing)}\n"
                                + $"  second: {FirstLine(body)}");
                        }
                        continue;
                    }
                    exports[name] = body;
                    exportOrder.Add(name);
                    continue;
                }

                // Unrecognised block (e.g. a leading comment) — drop. Header
                // comments from individual emits are noise once aggregated.
            }
        }

        // Reorder so enum declarations land first, then interfaces (shapes),
        // then consts / functions. TS enums create a temporal dead zone for
        // their member-value access — referencing `MyEnum.A` before
        // `export enum MyEnum { … }` is a compile-time error. Without this,
        // a static-readonly table literal in file A that references an enum
        // value from file B fails to type-check when B was emitted later.
        var enumNames = new List<string>();
        var interfaceNames = new List<string>();
        var otherNames = new List<string>();
        foreach (var name in exportOrder)
        {
            var block = exports[name];
            if (BlockStartsWith(block, "export enum") || BlockStartsWith(block, "enum "))
                enumNames.Add(name);
            else if (BlockStartsWith(block, "export interface") || BlockStartsWith(block, "interface "))
                interfaceNames.Add(name);
            else
                otherNames.Add(name);
        }

        var sb = new StringBuilder();
        foreach (var name in helperOrder)
        {
            sb.AppendLine(helpers[name]);
            sb.AppendLine();
        }
        var ordered = new List<string>();
        ordered.AddRange(enumNames);
        ordered.AddRange(interfaceNames);
        ordered.AddRange(otherNames);
        for (int i = 0; i < ordered.Count; i++)
        {
            sb.Append(exports[ordered[i]]);
            sb.AppendLine();
            if (i + 1 < ordered.Count) sb.AppendLine();
        }
        return sb.ToString();
    }

    // First non-empty line of an emitted block — enough to identify which
    // declaration is which in a collision message without dumping both bodies.
    static string FirstLine(string block)
    {
        foreach (var line in block.Split('\n'))
        {
            var trimmed = line.Trim('\r', ' ');
            if (trimmed.Length > 0) return trimmed;
        }
        return block.Trim();
    }

    static bool BlockStartsWith(string block, string prefix)
    {
        var trimmed = block.TrimStart('\n', '\r', ' ');
        return trimmed.StartsWith(prefix, System.StringComparison.Ordinal);
    }

    // Splits a Mirrorgen-emitted .ts into top-level blocks. A block is a
    // contiguous run terminated by a blank line at brace-depth 0; class
    // bodies (which include blank lines between members) stay intact because
    // their internal blank lines fall inside `{...}`.
    static IEnumerable<string> SplitTopLevelBlocks(string text)
    {
        var sb = new StringBuilder();
        int depth = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                if (depth == 0)
                {
                    if (sb.Length > 0)
                    {
                        yield return sb.ToString();
                        sb.Clear();
                    }
                    continue;
                }
                sb.Append('\n');
                continue;
            }
            sb.Append(line).Append('\n');
            foreach (var ch in line)
            {
                if (ch == '{') depth++;
                else if (ch == '}') depth = System.Math.Max(0, depth - 1);
            }
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
