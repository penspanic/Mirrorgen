using System;

namespace Mirrorgen;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class GenerateCrossTestAttribute : Attribute
{
    public int Samples { get; set; }
    public int Seed { get; set; }
}
