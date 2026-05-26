using System;

namespace Mirrorgen;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class TranspileAssemblyAttribute : Attribute
{
}
