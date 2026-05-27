using System;
using System.IO;
using System.Reflection;
using Microsoft.Build.Framework;
using Mirrorgen;
using Mirrorgen.Core;

namespace Mirrorgen.MSBuild;

/// <summary>
/// Loads the just-built assembly, walks every `[GenerateCrossTest]` method,
/// and writes a per-source fixtures JSON file alongside the emitted TS so
/// the TypeScript side can cross-validate against C# expected outputs.
/// </summary>
public sealed class MirrorgenFixturesTask : Microsoft.Build.Utilities.Task
{
    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Fully-qualified IMirrorgenExtension type; same value as MirrorgenConfig on the transpile task.</summary>
    public string MirrorgenConfig { get; set; } = string.Empty;

    public override bool Execute()
    {
        if (!File.Exists(AssemblyPath))
        {
            Log.LogError($"Mirrorgen: assembly not found at '{AssemblyPath}'.");
            return false;
        }

        try
        {
            PluginAssemblyResolver.EnsureRegistered();
            var asm = Assembly.LoadFrom(Path.GetFullPath(AssemblyPath));
            var registry = LoadRegistry(asm);

            var fixtures = FixtureGenerator.GenerateForAssembly(asm, registry);
            var json = FixtureGenerator.SerializeToJson(fixtures, registry);

            var dir = Path.GetDirectoryName(Path.GetFullPath(OutputPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(OutputPath, json);

            Log.LogMessage(
                MessageImportance.High,
                $"Mirrorgen fixtures: {fixtures.Count} method(s) -> {OutputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: false);
            return false;
        }
    }

    TypeMappingRegistry LoadRegistry(Assembly assembly)
    {
        if (string.IsNullOrEmpty(MirrorgenConfig)) return TypeMappingRegistry.Empty;
        var configType = assembly.GetType(MirrorgenConfig)
            ?? throw new InvalidOperationException(
                $"Mirrorgen: MirrorgenConfig type '{MirrorgenConfig}' not found in '{AssemblyPath}'.");
        var instance = Activator.CreateInstance(configType)
            ?? throw new InvalidOperationException(
                $"Mirrorgen: failed to instantiate '{MirrorgenConfig}'.");
        if (instance is not IMirrorgenExtension extension)
        {
            throw new InvalidOperationException(
                $"Mirrorgen: '{MirrorgenConfig}' does not implement IMirrorgenExtension.");
        }
        var builder = new MirrorgenBuilder();
        extension.Configure(builder);
        return builder.Build();
    }
}
