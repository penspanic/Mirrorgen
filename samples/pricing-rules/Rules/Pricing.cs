using Mirrorgen;

namespace Mirrorgen.Samples.PricingRules;

public static class Pricing
{
    // Switch expression with enum-member patterns. The discount is expressed
    // in basis points (1/100 of a percent) so it stays in integer land.
    [Transpile, GenerateCrossTest(Samples = 16, Seed = 101)]
    public static int TierDiscountBps(CustomerTier tier)
    {
        return tier switch
        {
            CustomerTier.Bronze => 0,
            CustomerTier.Silver => 250,
            CustomerTier.Gold => 750,
            CustomerTier.Platinum => 1500,
            _ => 0,
        };
    }

    // Switch expression with relational patterns. Each band picks a flat
    // shipping fee from a distance.
    [Transpile, GenerateCrossTest(Samples = 16, Seed = 102)]
    public static int ShippingFeeCents(int distanceKm)
    {
        return distanceKm switch
        {
            < 0 => 0,
            < 50 => 300,
            < 200 => 800,
            < 1000 => 1800,
            _ => 4500,
        };
    }

    // Tax expressed in basis points, applied with int wrap semantics. The
    // intermediate multiplication fits int32 for sane prices; the wrap stays
    // byte-equivalent for the pathological ones the random sampler picks.
    [Transpile, GenerateCrossTest(Samples = 16, Seed = 103)]
    public static int ApplyTaxCents(int amountCents, int taxBps)
    {
        int clampedBps = taxBps switch
        {
            < 0 => 0,
            > 10000 => 10000,
            _ => taxBps,
        };
        return amountCents + (amountCents * clampedBps / 10000);
    }

    // Composes TierDiscountBps + ApplyTaxCents + ShippingFeeCents into the
    // final price a customer sees. Exercises the [Transpile] -> [Transpile]
    // method-call chain through three helpers.
    [Transpile, GenerateCrossTest(Samples = 20, Seed = 104)]
    public static int FinalPriceCents(int subtotalCents, CustomerTier tier, int taxBps, int distanceKm)
    {
        int discountBps = TierDiscountBps(tier);
        int afterDiscount = subtotalCents - (subtotalCents * discountBps / 10000);
        int afterTax = ApplyTaxCents(afterDiscount, taxBps);
        return afterTax + ShippingFeeCents(distanceKm);
    }

    // A bounded while loop. The accumulator halves the value each round
    // (integer division) and counts how many rounds it takes to reach zero.
    // Cross-validates the loop semantics + integer division wrap.
    [Transpile, GenerateCrossTest(Samples = 16, Seed = 105)]
    public static int HalvingRounds(int value)
    {
        int v = value switch
        {
            < 0 => -value,
            _ => value,
        };
        if (v > 1_000_000) v = 1_000_000;
        int rounds = 0;
        while (v > 0)
        {
            v = v / 2;
            rounds++;
            if (rounds > 64) break;
        }
        return rounds;
    }

    // Records flow through with the plugin-mapped wrappers unwrapped. The
    // sampler builds a fresh OrderLine instance with random ProductId /
    // Quantity / Money; JSON serialisation emits plain `{Product, Quantity,
    // UnitPrice}` with each value as a number.
    [Transpile, GenerateCrossTest(Samples = 16, Seed = 106)]
    public static int LineSubtotalCents(OrderLine line)
    {
        int qty = line.Quantity switch
        {
            < 0 => 0,
            > 999 => 999,
            _ => line.Quantity,
        };
        return line.UnitPrice.Cents * qty;
    }

    // Switch with `and` composite pattern + enum.
    [Transpile, GenerateCrossTest(Samples = 16, Seed = 107)]
    public static ShippingZone ZoneForDistance(int distanceKm)
    {
        return distanceKm switch
        {
            >= 0 and < 50 => ShippingZone.Local,
            >= 50 and < 1000 => ShippingZone.Regional,
            _ => ShippingZone.International,
        };
    }
}
