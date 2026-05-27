using Mirrorgen;

namespace Mirrorgen.Samples.PricingRules;

/// <summary>
/// Maps the three single-field wrapper structs in Domain.cs onto a TS
/// <c>number</c>. The MSBuild target unwraps them everywhere — type
/// declaration, method signature, and cross-test fixture JSON.
/// </summary>
public sealed class MirrorgenConfig : IMirrorgenExtension
{
    public void Configure(IMirrorgenBuilder builder)
    {
        // OrderId / ProductId are pure-identifier wrappers — collapse to
        // TS `number`. Money keeps its declaration because the rules read
        // its `.Cents` member; mapping it would break the dot-access.
        builder.MapType<OrderId>().AsPrimitive("number");
        builder.MapType<ProductId>().AsPrimitive("number");
    }
}
