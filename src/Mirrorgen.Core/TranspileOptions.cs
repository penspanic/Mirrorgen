namespace Mirrorgen.Core;

public sealed class TranspileOptions
{
    public bool EmitValidators { get; init; }

    public static TranspileOptions Default { get; } = new();
}
