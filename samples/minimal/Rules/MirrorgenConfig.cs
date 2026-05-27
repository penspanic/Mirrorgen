using Mirrorgen;

namespace Mirrorgen.Samples.Minimal;

/// <summary>
/// Picked up by the Mirrorgen MSBuild task via the <c>MirrorgenConfig</c>
/// MSBuild property. Registers domain-type mappings so the generated TS
/// references <c>OrderId</c> as a plain <c>number</c> instead of carrying
/// the C# wrapper struct across the boundary.
/// </summary>
public sealed class MirrorgenConfig : IMirrorgenExtension
{
    public void Configure(IMirrorgenBuilder builder)
    {
        builder.MapType<OrderId>().AsPrimitive("number");
    }
}
