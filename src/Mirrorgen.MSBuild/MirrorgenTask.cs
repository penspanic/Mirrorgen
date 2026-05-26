using Microsoft.Build.Framework;

namespace Mirrorgen.MSBuild;

public sealed class MirrorgenTask : Microsoft.Build.Utilities.Task
{
    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.High, $"Mirrorgen {Core.TranspilerEngine.Version} — pre-alpha stub, no work performed.");
        return true;
    }
}
