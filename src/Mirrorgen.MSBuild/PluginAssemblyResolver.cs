using System.Reflection;
using System.Runtime.Loader;
using Mirrorgen;
using Mirrorgen.Core;

namespace Mirrorgen.MSBuild;

/// <summary>
/// Routes the user assembly's references to the Mirrorgen contract
/// assemblies back to the copies the task host already loaded. Without
/// this an <c>is IMirrorgenExtension</c> cast fails because the
/// type identity differs between the two ALCs.
/// </summary>
internal static class PluginAssemblyResolver
{
    static bool _registered;
    static readonly object _gate = new();

    public static void EnsureRegistered()
    {
        if (_registered) return;
        lock (_gate)
        {
            if (_registered) return;
            AssemblyLoadContext.Default.Resolving += OnResolving;
            _registered = true;
        }
    }

    static Assembly? OnResolving(AssemblyLoadContext ctx, AssemblyName name)
    {
        return name.Name switch
        {
            "Mirrorgen.Attributes" => typeof(IMirrorgenExtension).Assembly,
            "Mirrorgen.Core" => typeof(TypeMappingRegistry).Assembly,
            _ => null,
        };
    }
}
