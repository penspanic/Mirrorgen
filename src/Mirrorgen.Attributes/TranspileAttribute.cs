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

/// <summary>
/// Excludes a single member from a [Transpile]-marked type emit. Use on methods
/// (or get-only properties) that depend on C#-only constructs or unmirrored
/// types and have no place in the generated TS surface. Class-level [Transpile]
/// still emits the rest of the type as usual.
/// </summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field,
    AllowMultiple = false,
    Inherited = false)]
public sealed class NoTranspileAttribute : Attribute
{
}
