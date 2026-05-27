using System;

namespace Mirrorgen;

/// <summary>
/// User-implemented plugin entry point. The MSBuild task discovers a single
/// concrete implementation per project (named via the
/// <c>MirrorgenConfig</c> MSBuild property), instantiates it with a public
/// parameterless constructor, and calls <see cref="Configure"/> exactly once
/// so the project can register domain-type mappings.
/// </summary>
public interface IMirrorgenExtension
{
    void Configure(IMirrorgenBuilder builder);
}

/// <summary>
/// Fluent registry for telling the transpiler how to emit a C# type. The
/// builder is consumed during MSBuild discovery; the resulting mappings
/// flow into <c>Mirrorgen.Core.TranspilerEngine</c> alongside the source.
/// </summary>
public interface IMirrorgenBuilder
{
    ITsTypeBuilder MapType<T>();
    ITsTypeBuilder MapType(Type clrType);
}

/// <summary>
/// Sink for a single mapping target. A mapping is finalised the moment one
/// of these methods is called; further calls on the same builder are
/// rejected so a type can't be ambiguously mapped to two TS shapes.
/// </summary>
public interface ITsTypeBuilder
{
    /// <summary>
    /// Emit references to the C# type as a TS primitive (e.g. <c>number</c>,
    /// <c>string</c>, <c>boolean</c>). The C# type itself is never emitted.
    /// </summary>
    void AsPrimitive(string tsTypeName);

    /// <summary>
    /// Emit references to the C# type by the given TS type name, leaving
    /// the actual declaration to a runtime helper the consumer ships. The
    /// generated file does not declare the type — callers are expected to
    /// add the <c>import</c> by hand or via their bundler config.
    /// </summary>
    void RuntimeImport(string tsTypeName);
}
