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
                if (!HasAttribute(method, "Mirrorgen.GenerateCrossTestAttribute")) continue;
                if (!HasAttribute(method, "Mirrorgen.TranspileAttribute")) continue;
                results.Add(GenerateFor(method));
            }
        }
        return results;
    }

    public static FixtureRecord GenerateFor(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length > 0)
        {
            throw new NotSupportedException(
                $"FixtureGenerator v0 only supports parameterless methods; '{method.Name}' has {parameters.Length} parameter(s).");
        }
        if (!method.IsStatic)
        {
            throw new NotSupportedException(
                $"FixtureGenerator v0 only supports static methods; '{method.Name}' is instance.");
        }

        var expected = method.Invoke(null, Array.Empty<object?>());
        return new FixtureRecord(method.Name, new[] { new FixtureCall(Array.Empty<object?>(), expected) });
    }

    public static string SerializeToJson(IReadOnlyList<FixtureRecord> fixtures)
    {
        return JsonSerializer.Serialize(fixtures, JsonOptions);
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
