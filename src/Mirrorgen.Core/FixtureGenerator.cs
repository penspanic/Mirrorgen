using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirrorgen.Core;

public sealed record FixtureCall(
    [property: JsonPropertyName("args")] object?[] Args,
    [property: JsonPropertyName("expected")] object? Expected);

public sealed record FixtureRecord(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("calls")] IReadOnlyList<FixtureCall> Calls);

public static class FixtureGenerator
{
    const string TranspileAttributeName = "Mirrorgen.TranspileAttribute";
    const string GenerateCrossTestAttributeName = "Mirrorgen.GenerateCrossTestAttribute";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static IReadOnlyList<FixtureRecord> GenerateForAssembly(Assembly assembly)
        => GenerateForAssembly(assembly, TypeMappingRegistry.Empty);

    public static IReadOnlyList<FixtureRecord> GenerateForAssembly(Assembly assembly, TypeMappingRegistry registry)
    {
        var results = new List<FixtureRecord>();
        foreach (var type in SafeGetTypes(assembly))
        {
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!HasAttribute(method, GenerateCrossTestAttributeName)) continue;
                if (!HasAttribute(method, TranspileAttributeName)) continue;
                try
                {
                    results.Add(GenerateFor(method, registry));
                }
                catch (NotSupportedException ex)
                {
                    Console.Error.WriteLine($"[mirrorgen] skipped {type.FullName}.{method.Name}: {ex.Message}");
                }
            }
        }
        return results;
    }

    public static FixtureRecord GenerateFor(MethodInfo method)
        => GenerateFor(method, TypeMappingRegistry.Empty);

    public static FixtureRecord GenerateFor(MethodInfo method, TypeMappingRegistry registry)
    {
        if (!method.IsStatic)
        {
            throw new NotSupportedException(
                $"FixtureGenerator only supports static methods; '{method.Name}' is instance.");
        }

        var parameters = method.GetParameters();
        var (samples, seed) = ReadCrossTestSettings(method);

        if (parameters.Length == 0)
        {
            var expected = method.Invoke(null, Array.Empty<object?>());
            return new FixtureRecord(method.Name, new[] { new FixtureCall(Array.Empty<object?>(), expected) });
        }

        if (samples <= 0)
        {
            throw new NotSupportedException(
                $"Method '{method.Name}' has parameters but [GenerateCrossTest] does not specify Samples; set Samples to enable random argument generation.");
        }

        var rng = new Random(seed);
        var calls = new List<FixtureCall>(samples);
        for (int i = 0; i < samples; i++)
        {
            var args = new object?[parameters.Length];
            for (int p = 0; p < parameters.Length; p++)
            {
                args[p] = GenerateArg(parameters[p].ParameterType, parameters[p].Name ?? $"p{p}", method.Name, rng, registry);
            }
            var expected = method.Invoke(null, args);
            calls.Add(new FixtureCall(args, expected));
        }
        return new FixtureRecord(method.Name, calls);
    }

    public static string SerializeToJson(IReadOnlyList<FixtureRecord> fixtures)
        => SerializeToJson(fixtures, TypeMappingRegistry.Empty);

    public static string SerializeToJson(IReadOnlyList<FixtureRecord> fixtures, TypeMappingRegistry registry)
    {
        if (registry.Count == 0)
        {
            return JsonSerializer.Serialize(fixtures, JsonOptions);
        }
        var opts = new JsonSerializerOptions(JsonOptions);
        opts.Converters.Add(new WrappedPrimitiveConverter(registry));
        return JsonSerializer.Serialize(fixtures, opts);
    }

    /// <summary>
    /// JSON-time unwrap for types that the plugin registered as
    /// <see cref="ITsTypeBuilder.AsPrimitive"/>. The first ctor parameter
    /// (e.g. the <c>Value</c> in <c>record OrderId(int Value)</c>) is
    /// serialised in place of the whole object, so the JS side sees the
    /// same primitive it was told to expect by the type mapping.
    /// </summary>
    sealed class WrappedPrimitiveConverter : JsonConverter<object>
    {
        readonly TypeMappingRegistry _registry;
        public WrappedPrimitiveConverter(TypeMappingRegistry registry) { _registry = registry; }

        public override bool CanConvert(Type type)
        {
            var name = type.FullName;
            return name is not null
                && _registry.TryGet(name, out var m)
                && m.Kind == TypeMappingKind.Primitive;
        }

        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException("WrappedPrimitiveConverter is write-only.");

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            var t = value.GetType();
            var ctor = t.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();
            if (ctor is null || ctor.GetParameters().Length != 1)
            {
                throw new NotSupportedException(
                    $"Type '{t.FullName}' is registered as AsPrimitive but is not a single-field wrapper; cannot unwrap for JSON.");
            }
            var paramName = ctor.GetParameters()[0].Name
                ?? throw new InvalidOperationException("Unnamed ctor parameter.");
            var prop = t.GetProperty(paramName)
                ?? throw new InvalidOperationException($"Type '{t.FullName}' has no property '{paramName}' matching its ctor.");
            var inner = prop.GetValue(value);
            JsonSerializer.Serialize(writer, inner, options);
        }
    }

    static (int Samples, int Seed) ReadCrossTestSettings(MethodInfo method)
    {
        foreach (var attr in method.GetCustomAttributesData())
        {
            if (attr.AttributeType.FullName != GenerateCrossTestAttributeName) continue;
            int samples = 0, seed = 0;
            foreach (var na in attr.NamedArguments)
            {
                var value = na.TypedValue.Value;
                if (na.MemberName == "Samples" && value is int s) samples = s;
                else if (na.MemberName == "Seed" && value is int sd) seed = sd;
            }
            return (samples, seed);
        }
        return (0, 0);
    }

    static object GenerateArg(Type t, string paramName, string methodName, Random rng, TypeMappingRegistry registry)
    {
        // Plugin-mapped wrapper types (record OrderId(int Value) -> number)
        // sample as if they were their inner primitive. We still construct
        // the real wrapper so method invocation works; the JSON converter
        // unwraps it back to the primitive on serialisation.
        if (t.FullName is { } fullName && registry.TryGet(fullName, out var mapping) &&
            mapping.Kind == TypeMappingKind.Primitive)
        {
            var ctor = t.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();
            if (ctor is not null && ctor.GetParameters().Length == 1)
            {
                var innerType = ctor.GetParameters()[0].ParameterType;
                var inner = GenerateArg(innerType, paramName, methodName, rng, registry);
                return ctor.Invoke(new[] { inner });
            }
            throw new NotSupportedException(
                $"Type '{t.FullName}' is registered as AsPrimitive but isn't a single-field wrapper (method '{methodName}', parameter '{paramName}').");
        }

        // int sampling spans the full int32 range: the emitter wraps int arithmetic
        // with `| 0` / Math.imul so overflow stays wire-equivalent between C# unchecked
        // and JS. long / other widths stay narrow until they get the same treatment.
        if (t == typeof(int)) return rng.Next(int.MinValue, int.MaxValue);
        if (t == typeof(long)) return (long)rng.Next(-100_000, 100_001);
        if (t == typeof(short)) return (short)rng.Next(short.MinValue, short.MaxValue + 1);
        if (t == typeof(byte)) return (byte)rng.Next(0, 256);
        if (t == typeof(sbyte)) return (sbyte)rng.Next(sbyte.MinValue, sbyte.MaxValue + 1);
        if (t == typeof(uint)) return (uint)rng.Next(0, 10_001);
        if (t == typeof(ulong)) return (ulong)rng.Next(0, 100_001);
        if (t == typeof(ushort)) return (ushort)rng.Next(0, ushort.MaxValue + 1);
        if (t == typeof(bool)) return rng.Next(2) == 0;
        if (t == typeof(float)) return (float)(rng.NextDouble() * 200.0 - 100.0);
        if (t == typeof(double)) return rng.NextDouble() * 200.0 - 100.0;
        if (t == typeof(string)) return GenerateString(rng);

        // T[] / List<T> / IReadOnlyList<T> / IList<T> — length 0..8, each
        // element recursively sampled. JSON serialisation handles the array
        // shape, which is what the TS side reads as a `T[]`.
        if (t.IsArray && t.GetElementType() is { } arrayElem)
        {
            int length = rng.Next(0, 9);
            var arr = Array.CreateInstance(arrayElem, length);
            for (int i = 0; i < length; i++)
            {
                arr.SetValue(GenerateArg(arrayElem, paramName, methodName, rng, registry), i);
            }
            return arr;
        }
        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IReadOnlyList<>) || def == typeof(IList<>))
            {
                var elem = t.GetGenericArguments()[0];
                var listType = typeof(List<>).MakeGenericType(elem);
                var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
                int length = rng.Next(0, 9);
                for (int i = 0; i < length; i++)
                {
                    list.Add(GenerateArg(elem, paramName, methodName, rng, registry));
                }
                return list;
            }
        }

        if (t.IsEnum && HasAttribute(t, TranspileAttributeName))
        {
            var values = Enum.GetValues(t);
            return values.GetValue(rng.Next(values.Length))!;
        }

        if (HasAttribute(t, TranspileAttributeName))
        {
            return GenerateTranspileType(t, paramName, methodName, rng, registry);
        }

        throw new NotSupportedException(
            $"FixtureGenerator argument sampling does not yet support type '{t}' (method '{methodName}', parameter '{paramName}').");
    }

    static object GenerateTranspileType(Type t, string paramName, string methodName, Random rng, TypeMappingRegistry registry)
    {
        // Prefer the ctor with the most parameters — that's the positional
        // record primary ctor, or a class's only meaningful ctor. Body-only
        // members (init / get;set; properties added beyond the primary
        // ctor's parameters) are set via reflection afterwards.
        var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var ctor = ctors
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new NotSupportedException(
                $"Type '{t.FullName}' has no public constructor; FixtureGenerator can't sample it (method '{methodName}', parameter '{paramName}').");

        var ctorParams = ctor.GetParameters();
        var ctorArgs = ctorParams
            .Select(p => GenerateArg(p.ParameterType, p.Name ?? "_", methodName, rng, registry))
            .ToArray<object?>();
        var instance = ctor.Invoke(ctorArgs);

        var ctorParamNames = new HashSet<string>(
            ctorParams.Where(p => p.Name is not null).Select(p => p.Name!),
            StringComparer.Ordinal);

        foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (ctorParamNames.Contains(prop.Name)) continue;
            if (!prop.CanWrite || prop.GetSetMethod(nonPublic: false) is null) continue;
            prop.SetValue(instance, GenerateArg(prop.PropertyType, prop.Name, methodName, rng, registry));
        }

        foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (ctorParamNames.Contains(field.Name)) continue;
            field.SetValue(instance, GenerateArg(field.FieldType, field.Name, methodName, rng, registry));
        }

        return instance;
    }

    const string StringPool = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_.";

    static string GenerateString(Random rng)
    {
        int length = rng.Next(0, 17); // 0..16 inclusive
        if (length == 0) return string.Empty;
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            sb.Append(StringPool[rng.Next(StringPool.Length)]);
        }
        return sb.ToString();
    }

    static bool HasAttribute(MethodInfo method, string fullTypeName)
    {
        foreach (var attr in method.GetCustomAttributesData())
        {
            if (attr.AttributeType.FullName == fullTypeName) return true;
        }
        return false;
    }

    static bool HasAttribute(Type t, string fullTypeName)
    {
        foreach (var attr in t.GetCustomAttributesData())
        {
            if (attr.AttributeType.FullName == fullTypeName) return true;
        }
        return false;
    }

    static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
