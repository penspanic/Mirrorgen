using System;
using System.Collections.Generic;
using Mirrorgen;

namespace Mirrorgen.Core;

public enum TypeMappingKind
{
    Primitive,
    RuntimeImport,
}

public sealed record TypeMapping(string TsTypeName, TypeMappingKind Kind);

/// <summary>
/// Frozen lookup from C# type's fully-qualified name (without nullability /
/// generic adornments) to the TS shape it should emit as. Constructed by
/// running <see cref="IMirrorgenExtension"/> implementations through
/// <see cref="MirrorgenBuilder"/>; passed into <c>TranspilerEngine</c>.
/// </summary>
public sealed class TypeMappingRegistry
{
    public static TypeMappingRegistry Empty { get; } = new(new Dictionary<string, TypeMapping>(StringComparer.Ordinal));

    readonly IReadOnlyDictionary<string, TypeMapping> _byFullName;

    public TypeMappingRegistry(IReadOnlyDictionary<string, TypeMapping> byFullName)
    {
        _byFullName = byFullName;
    }

    public bool TryGet(string fullName, out TypeMapping mapping)
    {
        return _byFullName.TryGetValue(fullName, out mapping!);
    }

    public int Count => _byFullName.Count;
}

/// <summary>
/// Concrete builder driven by <see cref="IMirrorgenExtension.Configure"/>.
/// Records mappings keyed by the CLR type's <see cref="Type.FullName"/>.
/// </summary>
public sealed class MirrorgenBuilder : IMirrorgenBuilder
{
    readonly Dictionary<string, TypeMapping> _mappings = new(StringComparer.Ordinal);

    public ITsTypeBuilder MapType<T>() => MapType(typeof(T));

    public ITsTypeBuilder MapType(Type clrType)
    {
        var fullName = clrType.FullName
            ?? throw new ArgumentException($"Type '{clrType}' has no FullName; only closed, non-anonymous types are supported.", nameof(clrType));
        if (_mappings.ContainsKey(fullName))
        {
            throw new InvalidOperationException(
                $"Type '{fullName}' is already mapped; remove the duplicate MapType call.");
        }
        return new TsTypeSink(this, fullName);
    }

    public TypeMappingRegistry Build() => new(_mappings);

    sealed class TsTypeSink : ITsTypeBuilder
    {
        readonly MirrorgenBuilder _owner;
        readonly string _fullName;
        public TsTypeSink(MirrorgenBuilder owner, string fullName)
        {
            _owner = owner;
            _fullName = fullName;
        }
        public void AsPrimitive(string tsTypeName) => Record(tsTypeName, TypeMappingKind.Primitive);
        public void RuntimeImport(string tsTypeName) => Record(tsTypeName, TypeMappingKind.RuntimeImport);

        void Record(string tsTypeName, TypeMappingKind kind)
        {
            if (string.IsNullOrEmpty(tsTypeName))
            {
                throw new ArgumentException("TS type name must be non-empty.", nameof(tsTypeName));
            }
            _owner._mappings[_fullName] = new TypeMapping(tsTypeName, kind);
        }
    }
}
