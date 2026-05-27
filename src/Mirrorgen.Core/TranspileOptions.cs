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

    public static TranspileOptions Default { get; } = new();
}
