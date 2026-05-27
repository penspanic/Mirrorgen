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
            var options = string.IsNullOrEmpty(AggregateOutputFile)
                ? TranspileOptions.Default
                : new TranspileOptions { AggregateOutputFile = AggregateOutputFile };
            var result = BatchTranspiler.TranspileFiles(files, SourceRoot, OutputDirectory, registry, options);
            Log.LogMessage(
                MessageImportance.High,
                $"Mirrorgen {TranspilerEngine.Version}: wrote {result.WrittenCount} TS file(s), skipped {result.SkippedCount}.");
            return true;
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
