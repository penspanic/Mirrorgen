using System;
using System.IO;
using System.Reflection;
using Microsoft.Build.Framework;
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

    public override bool Execute()
    {
        if (!File.Exists(AssemblyPath))
        {
            Log.LogError($"Mirrorgen: assembly not found at '{AssemblyPath}'.");
            return false;
        }

        try
        {
            // LoadFrom resolves co-located dependencies (Mirrorgen.Attributes etc.)
            // from the assembly's own bin directory.
            var asm = Assembly.LoadFrom(Path.GetFullPath(AssemblyPath));
            var fixtures = FixtureGenerator.GenerateForAssembly(asm);
            var json = FixtureGenerator.SerializeToJson(fixtures);

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
}
