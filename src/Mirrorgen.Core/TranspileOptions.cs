namespace Mirrorgen.Core;

public sealed class TranspileOptions
{
    public bool EmitValidators { get; init; }

    /// <summary>
    /// Path substrings that mark a syntax tree as a directory-scan source — any
    /// public type declared in a file whose path contains one of these markers
    /// is treated as if it had `[Transpile]`. Methods are NOT auto-emitted in
    /// this mode; matches TsGen's `--data-dir-marker` "shape only" semantics.
    /// </summary>
    public IReadOnlyList<string> ScanPathMarkers { get; init; } = Array.Empty<string>();

    /// <summary>
    /// When non-null, BatchTranspiler emits a single aggregated .ts at this
    /// filename (under the output directory) instead of one .ts per source.
    /// Mirrors TsGen's `--types-file` single-output shape — required so
    /// existing OFF.Client.Web consumers importing from `./generated/off-network`
    /// keep working after cutover.
    /// </summary>
    public string? AggregateOutputFile { get; init; }

    public static TranspileOptions Default { get; } = new();
}
