using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Build.Framework;
using Mirrorgen;
using Mirrorgen.Core;

namespace Mirrorgen.MSBuild;

public sealed class MirrorgenTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] Sources { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// Extra .cs files to include in the Mirrorgen scan without contributing to
    /// the host project's @(Compile). Lets one csproj aggregate-emit DTOs
    /// declared in sibling csprojs (e.g. Networking.WebServices) without
    /// double-compiling those sources into the host assembly.
    /// </summary>
    public ITaskItem[] AdditionalSources { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string SourceRoot { get; set; } = string.Empty;

    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Path of the just-built assembly. Required when MirrorgenConfig is set.</summary>
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>Fully-qualified name of the IMirrorgenExtension implementation, if any.</summary>
    public string MirrorgenConfig { get; set; } = string.Empty;

    /// <summary>
    /// When set, BatchTranspiler aggregates every emitted .ts into this
    /// single filename under <see cref="OutputDirectory"/>. Matches TsGen's
    /// single-file output shape for cutover compatibility (e.g. "off-network.ts").
    /// </summary>
    public string AggregateOutputFile { get; set; } = string.Empty;

    /// <summary>
    /// Extension used in the module specifier of emitted cross-file imports.
    /// Defaults to ".js", which is what Node16 / NodeNext ESM requires and
    /// what bundler / classic resolution also accept. Set to ".ts" for a
    /// consumer with `allowImportingTsExtensions`, or to "none" for
    /// extensionless specifiers.
    /// </summary>
    public string ImportExtension { get; set; } = string.Empty;

    /// <summary>
    /// When true, append lightweight parseX(value) validators to the emitted
    /// TypeScript output. The validators normalize omitted nullable members to
    /// null so parsed wire documents satisfy the generated interface contract.
    /// </summary>
    public bool EmitValidators { get; set; }

    /// <summary>
    /// When set, the same source set is also transpiled to WGSL and written to
    /// this single file. WGSL is a different surface language (GPU shaders), so
    /// it is opt-in and type-scoped via <see cref="WgslTypes"/> — only the
    /// listed types emit, leaving TypeScript-only [Transpile] types alone.
    /// </summary>
    public string WgslOutputFile { get; set; } = string.Empty;

    /// <summary>Type names (simple, unqualified) to emit as WGSL. Required when
    /// <see cref="WgslOutputFile"/> is set; an empty set would try to emit every
    /// [Transpile] type, including ones the WGSL backend can't render.</summary>
    public ITaskItem[] WgslTypes { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// When set (and <see cref="WgslOutputFile"/> ends in .ts), the WGSL is
    /// wrapped as a TypeScript module exporting this identifier as a string
    /// constant — matching the renderer's convention where every shader is a
    /// `.wgsl.ts` template-literal module (no raw-.wgsl text loader). When
    /// empty, the raw WGSL is written verbatim.
    /// </summary>
    public string WgslExportName { get; set; } = string.Empty;

    public override bool Execute()
    {
        if (string.IsNullOrEmpty(OutputDirectory))
        {
            Log.LogMessage(MessageImportance.Low, "Mirrorgen: MirrorgenOutput not set, skipping.");
            return true;
        }

        try
        {
            var registry = LoadRegistry();
            var files = Sources.Select(item => item.GetMetadata("FullPath")).ToList();
            if (AdditionalSources.Length > 0)
            {
                files.AddRange(AdditionalSources.Select(item => item.GetMetadata("FullPath")));
            }
            var options = new TranspileOptions
            {
                AggregateOutputFile = string.IsNullOrEmpty(AggregateOutputFile) ? null : AggregateOutputFile,
                EmitValidators = EmitValidators,
                // MSBuild cannot carry an intentionally empty string through a
                // property, so "none" is the spelling for "no extension".
                ImportExtension = string.IsNullOrEmpty(ImportExtension)
                    ? new TranspileOptions().ImportExtension
                    : (string.Equals(ImportExtension, "none", System.StringComparison.OrdinalIgnoreCase)
                        ? string.Empty
                        : ImportExtension),
            };
            var result = BatchTranspiler.TranspileFiles(files, SourceRoot, OutputDirectory, registry, options);
            Log.LogMessage(
                MessageImportance.High,
                $"Mirrorgen {TranspilerEngine.Version}: wrote {result.WrittenCount} TS file(s), "
                + $"{result.UnchangedCount} already current, skipped {result.SkippedCount} "
                + "source(s) with nothing to emit.");

            if (!string.IsNullOrEmpty(WgslOutputFile))
            {
                var typeNames = WgslTypes.Select(t => t.ItemSpec).Where(s => !string.IsNullOrEmpty(s)).ToArray();
                if (typeNames.Length == 0)
                {
                    Log.LogError("Mirrorgen: WgslOutputFile is set but no WgslTypes were provided — WGSL emission must be type-scoped.");
                    return false;
                }
                var wgsl = TranspilerEngine.TranspileFilesToWgsl(files, typeNames);
                if (!string.IsNullOrEmpty(WgslExportName))
                {
                    // Wrap as a TS module so the renderer imports it like every
                    // other `.wgsl.ts` shader. String.raw keeps backslashes /
                    // ${ } literal — WGSL has neither, but it's the safe choice.
                    wgsl =
                        "// <auto-generated> Mirrorgen WGSL from C# [Transpile]. Do not edit.\n"
                        + $"export const {WgslExportName} = String.raw`\n{wgsl}`;\n";
                }
                GeneratedFile.Write(WgslOutputFile, wgsl);
                Log.LogMessage(
                    MessageImportance.High,
                    $"Mirrorgen {TranspilerEngine.Version}: wrote WGSL for [{string.Join(", ", typeNames)}] to {WgslOutputFile}.");
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // MSBuild would otherwise surface the raw "the process cannot access
            // the file ... because it is being used by another process", which
            // names a file and no cause — it reads as a defect in the project
            // being built, and the usual response is to rerun the job. The task
            // knows the two things worth saying: where it was writing, and that
            // a shared output path is what makes another writer possible.
            Log.LogError(
                $"Mirrorgen could not write its output under '{OutputDirectory}': {ex.Message} "
                + "This is what a second writer on the same path looks like — another build of "
                + "this project (a parallel CI job, a design-time build, a stale MSBuild node), "
                + "or an editor holding the file. Mirrorgen writes atomically and skips output "
                + "that is already current, so a lock outlasting that is something holding the "
                + "file open rather than a momentary overlap. Give each build its own path by "
                + "setting MirrorgenOutput under $(IntermediateOutputPath) or another "
                + "per-project intermediate directory.");
            return false;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: false);
            return false;
        }
    }

    TypeMappingRegistry LoadRegistry()
    {
        if (string.IsNullOrEmpty(MirrorgenConfig)) return TypeMappingRegistry.Empty;
        if (string.IsNullOrEmpty(AssemblyPath) || !File.Exists(AssemblyPath))
        {
            Log.LogWarning(
                $"Mirrorgen: MirrorgenConfig is '{MirrorgenConfig}' but the assembly path '{AssemblyPath}' is missing — skipping plugin discovery.");
            return TypeMappingRegistry.Empty;
        }

        // Force the user assembly to bind to the same Mirrorgen.Attributes
        // instance the task host already loaded — otherwise the cast to
        // IMirrorgenExtension fails because each context has its own copy
        // of the interface type.
        PluginAssemblyResolver.EnsureRegistered();

        var asm = Assembly.LoadFrom(Path.GetFullPath(AssemblyPath));
        var configType = asm.GetType(MirrorgenConfig)
            ?? throw new InvalidOperationException(
                $"Mirrorgen: MirrorgenConfig type '{MirrorgenConfig}' not found in '{AssemblyPath}'.");
        var instance = Activator.CreateInstance(configType)
            ?? throw new InvalidOperationException(
                $"Mirrorgen: failed to instantiate '{MirrorgenConfig}' — make sure it has a public parameterless constructor.");
        if (instance is not IMirrorgenExtension extension)
        {
            throw new InvalidOperationException(
                $"Mirrorgen: '{MirrorgenConfig}' does not implement IMirrorgenExtension. Make sure both the task host and the user assembly reference the same Mirrorgen.Attributes binary.");
        }
        var builder = new MirrorgenBuilder();
        extension.Configure(builder);
        var registry = builder.Build();
        Log.LogMessage(
            MessageImportance.High,
            $"Mirrorgen: loaded plugin '{MirrorgenConfig}' with {registry.Count} mapping(s).");
        return registry;
    }
}
