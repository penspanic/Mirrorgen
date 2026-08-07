using Mirrorgen;

namespace Mirrorgen.Samples.Minimal;

[Transpile]
public enum DiscountKind
{
    None,
    Flat,
    Percent,
}

// Domain primitive — the IMirrorgenExtension in MirrorgenConfig.cs maps
// this onto the TS `number` so the generated code never sees the wrapper.
public readonly record struct OrderId(int Value);

[Transpile]
public record OrderLine(OrderId Id, int Quantity, int UnitPrice, DiscountKind Kind, int DiscountValue);

public static class LineMath
{
    // Exercises the [Transpile] record + enum cross-test sampling end to end.
    // Random OrderLine instances drive both sides; vitest asserts byte
    // equivalence on the int-clamped result.
    [Transpile, GenerateCrossTest(Samples = 16, Seed = 4)]
    public static int LineSubtotal(OrderLine line)
    {
        int qty = Pricing.ClampQuantity(line.Quantity, 100);
        int subtotal = Pricing.Total(line.UnitPrice, qty);
        return line.Kind switch
        {
            DiscountKind.None => subtotal,
            DiscountKind.Percent => Pricing.ApplyDiscount(subtotal, line.DiscountValue),
            DiscountKind.Flat => subtotal - line.DiscountValue,
            _ => subtotal,
        };
    }
}

// Three [Transpile] methods that survive the v0.1 walker subset:
//   - locals + arithmetic with int32 wrap
//   - control flow with ternary
//   - method composition across [Transpile] -> [Transpile]
//
// Each method also carries [GenerateCrossTest] so the build emits a JSON
// fixture and the TS test in client/test verifies byte-equivalence with C#.
public static class Pricing
{
    [Transpile]
    [GenerateCrossTest(Samples = 16, Seed = 1)]
    // Explicit corner cases run before the random samples so the JSON's
    // first rows are the ones a human can sanity-check immediately.
    [CrossTestCase(int.MinValue, 100)]
    [CrossTestCase(int.MaxValue, 100)]
    [CrossTestCase(0, 0)]
    public static int ClampQuantity(int requested, int max)
    {
        if (requested < 0) return 0;
        if (requested > max) return max;
        return requested;
    }

    [Transpile, GenerateCrossTest(Samples = 16, Seed = 2)]
    public static int Total(int unitPrice, int quantity)
    {
        return unitPrice * quantity;
    }

    [Transpile, GenerateCrossTest(Samples = 16, Seed = 3)]
    public static int ApplyDiscount(int total, int discountPct)
    {
        int pct = discountPct < 0 ? 0 : (discountPct > 100 ? 100 : discountPct);
        return total * (100 - pct) / 100;
    }

    // Array indexing — the emit carries `!` on element reads so the generated
    // TS still typechecks under the client's `noUncheckedIndexedAccess`.
    [Transpile, GenerateCrossTest(Samples = 16, Seed = 5)]
    [CrossTestCase(0)]
    [CrossTestCase(4)]
    public static int TierTotal(int tier)
    {
        var tiers = new int[] { 100, 250, 500, 1000 };
        if (tier < 0 || tier >= tiers.Length) return 0;
        int total = 0;
        for (int i = 0; i <= tier; i++) total += tiers[i];
        return total;
    }
}
