using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

/// <summary>
/// Golden-file equivalence between TsGen (current OFF tooling) and Mirrorgen
/// (its replacement) for representative DTO surface. The fixture under
/// `Fixtures/TsGenParity/source/` is run through TsGen once and committed
/// as `golden/dtos.ts`; this test runs the same source through Mirrorgen
/// and asserts the emit is equivalent after normalising header comment +
/// indentation differences.
/// </summary>
public class TsGenParityTests
{
    static string FixtureDir =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TsGenParity");

    [Fact]
    public void Mirrorgen_Emit_Matches_TsGen_Golden_Normalised()
    {
        var sourceDir = Path.Combine(FixtureDir, "source");
        var goldenPath = Path.Combine(FixtureDir, "golden", "dtos.ts");

        Assert.True(Directory.Exists(sourceDir),
            $"Fixture source dir not deployed to test bin: {sourceDir}");
        Assert.True(File.Exists(goldenPath),
            $"Golden TsGen output not deployed: {goldenPath}");

        var outDir = Path.Combine(Path.GetTempPath(), "mirrorgen-tsgenparity-" + Guid.NewGuid().ToString("N"));
        try
        {
            BatchTranspiler.TranspileDirectory(sourceDir, outDir);
            var emitted = Path.Combine(outDir, "Dtos.ts");
            Assert.True(File.Exists(emitted), $"Mirrorgen did not emit {emitted}");

            var golden = Normalise(File.ReadAllText(goldenPath));
            var mirror = Normalise(File.ReadAllText(emitted));

            if (golden != mirror)
            {
                throw new Xunit.Sdk.XunitException(
                    "Mirrorgen emit diverges from TsGen golden.\n\n" +
                    "--- golden (normalised) ---\n" + golden + "\n\n" +
                    "--- mirrorgen (normalised) ---\n" + mirror);
            }
        }
        finally
        {
            try { Directory.Delete(outDir, recursive: true); } catch { }
        }
    }

    // TsGen emits a header comment + 4-space indent + trailing blank lines.
    // Mirrorgen emits no header + 2-space indent. Both differences are
    // cosmetic — strip them and compare the rest exactly.
    static string Normalise(string text)
    {
        var lines = text.Split('\n');
        var keep = new List<string>(lines.Length);
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("// Auto-generated", StringComparison.Ordinal)) continue;
            if (trimmed.Length == 0) continue;
            keep.Add(trimmed);
        }
        return string.Join("\n", keep);
    }
}
