using Mirrorgen;

namespace Mirrorgen.Samples.Minimal;

// Three [Transpile] methods that survive the v0.1 walker subset:
//   - locals + arithmetic with int32 wrap
//   - control flow with ternary
//   - method composition across [Transpile] -> [Transpile]
//
// Each method also carries [GenerateCrossTest] so the build emits a JSON
// fixture and the TS test in client/test verifies byte-equivalence with C#.
public static class Pricing
{
    [Transpile, GenerateCrossTest(Samples = 16, Seed = 1)]
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
}
