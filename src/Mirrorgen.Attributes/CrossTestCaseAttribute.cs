using System;

namespace Mirrorgen;

/// <summary>
/// Adds an explicit input row to a method's cross-test fixture, alongside
/// the random samples produced by <see cref="GenerateCrossTestAttribute"/>.
/// Multiple <c>[CrossTestCase]</c> attributes stack — each one becomes one
/// extra fixture call. The argument values must be compile-time constants
/// (the usual C# attribute restriction).
/// </summary>
/// <example>
/// [Transpile, GenerateCrossTest(Samples = 16, Seed = 1)]
/// [CrossTestCase(int.MinValue, 0)]
/// [CrossTestCase(int.MaxValue, int.MinValue)]
/// public static int Sub(int a, int b) => a - b;
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class CrossTestCaseAttribute : Attribute
{
    public object?[] Args { get; }
    public CrossTestCaseAttribute(params object?[] args)
    {
        Args = args ?? Array.Empty<object?>();
    }
}
