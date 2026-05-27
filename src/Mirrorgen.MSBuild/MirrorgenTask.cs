using System;
using System.Linq;
using Microsoft.Build.Framework;
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

    public override bool Execute()
    {
        if (string.IsNullOrEmpty(OutputDirectory))
        {
            Log.LogMessage(MessageImportance.Low, "Mirrorgen: MirrorgenOutput not set, skipping.");
            return true;
        }

        try
        {
            var files = Sources.Select(item => item.GetMetadata("FullPath")).ToList();
            var result = BatchTranspiler.TranspileFiles(files, SourceRoot, OutputDirectory);
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
}
