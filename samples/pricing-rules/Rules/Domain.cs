using Mirrorgen;

namespace Mirrorgen.Samples.PricingRules;

// Domain shapes live in their own file. The walker's reachability scan
// runs across the whole compilation now, so methods in Pricing.cs that
// reference these types still emit them into the same Pricing.ts file.

[Transpile]
public record Money(int Cents);
public readonly record struct OrderId(int Value);
public readonly record struct ProductId(int Value);

[Transpile]
public enum CustomerTier
{
    Bronze = 0,
    Silver = 1,
    Gold = 2,
    Platinum = 3,
}

[Transpile]
public enum ShippingZone
{
    Local = 0,
    Regional = 1,
    International = 2,
}

[Transpile]
public record OrderLine(ProductId Product, int Quantity, Money UnitPrice);
