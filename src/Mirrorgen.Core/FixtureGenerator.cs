using System.Reflection;
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
                    results.Add(GenerateFor(method));
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
                args[p] = GenerateArg(parameters[p].ParameterType, parameters[p].Name ?? $"p{p}", method.Name, rng);
            }
            var expected = method.Invoke(null, args);
            calls.Add(new FixtureCall(args, expected));
        }
        return new FixtureRecord(method.Name, calls);
    }

    public static string SerializeToJson(IReadOnlyList<FixtureRecord> fixtures)
    {
        return JsonSerializer.Serialize(fixtures, JsonOptions);
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

    static object GenerateArg(Type t, string paramName, string methodName, Random rng)
    {
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
        throw new NotSupportedException(
            $"FixtureGenerator argument sampling does not yet support type '{t}' (method '{methodName}', parameter '{paramName}').");
    }

    static bool HasAttribute(MethodInfo method, string fullTypeName)
    {
        foreach (var attr in method.GetCustomAttributesData())
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
