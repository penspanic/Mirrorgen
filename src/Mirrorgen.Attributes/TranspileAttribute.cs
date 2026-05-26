using System;

namespace Mirrorgen;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum
        | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property,
    AllowMultiple = false,
    Inherited = false)]
public sealed class TranspileAttribute : Attribute
{
    public string? EmitName { get; set; }
}
