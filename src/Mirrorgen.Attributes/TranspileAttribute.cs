using System;

namespace Mirrorgen;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum
        | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property
        | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
public sealed class TranspileAttribute : Attribute
{
    public string? EmitName { get; set; }

    public TranspileShape Shape { get; set; } = TranspileShape.Interface;
}

public enum TranspileShape
{
    Interface = 0,
    Class = 1,
}
