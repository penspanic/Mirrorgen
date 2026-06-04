using System;
using System.Diagnostics;
using System.IO;

namespace Mirrorgen.Tests;

// Locates the `naga` WGSL validator and runs it over generated source. Naga
// is the real correctness gate for the WGSL backend — substring assertions
// prove shape, naga proves the module actually compiles (types unify,
// bindings are well-formed, builtins exist). Absent on a machine without it,
// validation is skipped (the test passes with a console note) so CI without
// the toolchain doesn't hard-fail; CI/dev with naga installed gets the gate.
static class WgslNaga
{
    public static string? Path { get; } = Locate();

    static string? Locate()
    {
        var env = Environment.GetEnvironmentVariable("NAGA");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cargo = System.IO.Path.Combine(home, ".cargo", "bin", "naga");
        if (File.Exists(cargo)) return cargo;

        // PATH lookup.
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(System.IO.Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var candidate = System.IO.Path.Combine(dir, "naga");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static bool Available => Path is not null;

    /// <summary>Validate WGSL source. Returns (success, output). Writes the
    /// source to a temp .wgsl file because naga validates by path.</summary>
    public static (bool ok, string output) Validate(string wgsl)
    {
        if (Path is null) throw new InvalidOperationException("naga not available");
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "mirrorgen-naga-" + Guid.NewGuid().ToString("N") + ".wgsl");
        File.WriteAllText(tmp, wgsl);
        try
        {
            var psi = new ProcessStartInfo(Path)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(tmp);
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return (proc.ExitCode == 0, stdout + stderr);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
        }
    }
}
