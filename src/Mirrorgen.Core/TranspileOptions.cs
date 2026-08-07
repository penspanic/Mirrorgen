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

    /// <summary>
    /// Extension used in the module specifier of emitted cross-file imports.
    ///
    /// The emitted .ts is library code that ships to npm consumers, so the
    /// default is what the strictest of them needs: Node16 / NodeNext ESM
    /// requires a `.js` specifier and resolves it back to the `.ts`. Bundler
    /// and classic resolution accept that too, so `.js` is the only value that
    /// works everywhere unmodified.
    ///
    /// Set to <c>".ts"</c> for a consumer with `allowImportingTsExtensions`,
    /// or to the empty string for extensionless specifiers.
    /// </summary>
    public string ImportExtension { get; init; } = ".js";

    public static TranspileOptions Default { get; } = new();
}
