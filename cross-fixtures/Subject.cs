using System.Collections.Generic;
using Mirrorgen;

namespace Mirrorgen.CrossFixtures;

public static class Subject
{
    [Transpile, GenerateCrossTest]
    public static int FortyTwo() => 42;

    [Transpile, GenerateCrossTest]
    public static int AddSeven() => 35 + 7;

    [Transpile, GenerateCrossTest]
    public static int Ternary() => 5 > 3 ? 10 : 20;

    [Transpile, GenerateCrossTest]
    public static bool StrictEquality() => 1 == 1;

    [Transpile, GenerateCrossTest]
    public static string Greeting() => "hi";

    [Transpile, GenerateCrossTest]
    public static double Pi() => 3.14;

    [Transpile, GenerateCrossTest(Samples = 10, Seed = 1)]
    public static int AddOne(int x) => x + 1;

    [Transpile, GenerateCrossTest(Samples = 6, Seed = 2)]
    public static bool Both(bool a, bool b) => a && b;

    [Transpile, GenerateCrossTest(Samples = 8, Seed = 3)]
    public static double Triple(double v) => v * 3.0;

    [Transpile, GenerateCrossTest(Samples = 12, Seed = 5)]
    public static int Sign(int x)
    {
        if (x > 0) return 1;
        else if (x < 0) return -1;
        else return 0;
    }

    [Transpile, GenerateCrossTest(Samples = 8, Seed = 6)]
    public static int Max(int a, int b)
    {
        if (a > b) {
            return a;
        }
        return b;
    }

    // For-loop demo. n is clamped to [0, 100] so cross-validation samples
    // (full int32 range) can't trigger million-iteration loops on the JS side.
    [Transpile, GenerateCrossTest(Samples = 10, Seed = 19)]
    public static int SumTo(int n)
    {
        int top = n > 100 ? 100 : (n < 0 ? 0 : n);
        int sum = 0;
        for (int i = 0; i < top; i++) sum += i;
        return sum;
    }

    // String parameter — exercises the string-sampling path.
    [Transpile, GenerateCrossTest(Samples = 8, Seed = 17)]
    public static string Echo(string s) => s;

    // Composition — IncTwice calls AddOne twice via the [Transpile] -> [Transpile] path.
    [Transpile, GenerateCrossTest(Samples = 8, Seed = 11)]
    public static int IncTwice(int x) => AddOne(AddOne(x));

    // README hero example: locals + int arithmetic wrap + comparison.
    // Range is tightened on the test side (int32 squared overflows otherwise);
    // here we just prove the emit + cross-validation runs end to end.
    [Transpile, GenerateCrossTest(Samples = 16, Seed = 8)]
    public static bool IsWithinDistance(int x1, int y1, int x2, int y2, int radius)
    {
        int dx = x2 - x1;
        int dy = y2 - y1;
        return dx * dx + dy * dy <= radius * radius;
    }

    // Math.* whitelist round-trip. Math.Max composed with int wrap exercises
    // both the System.Math mapping and the multiplication-wrap path on the
    // result of an external call.
    [Transpile, GenerateCrossTest(Samples = 12, Seed = 23)]
    public static int MaxThenDouble(int a, int b)
    {
        return System.Math.Max(a, b) * 2;
    }

    [Transpile, GenerateCrossTest(Samples = 12, Seed = 24)]
    public static int Clamp(int v, int lo, int hi)
    {
        return System.Math.Min(System.Math.Max(v, lo), hi);
    }

    // switch expression on int constants.
    [Transpile, GenerateCrossTest(Samples = 12, Seed = 25)]
    public static int CategoryByMod(int x)
    {
        int m = x % 3;
        if (m < 0) m += 3;
        return m switch
        {
            0 => 100,
            1 => 200,
            _ => 300,
        };
    }

    // switch statement on int constants.
    [Transpile, GenerateCrossTest(Samples = 10, Seed = 26)]
    public static string LabelMod4(int x)
    {
        int m = x % 4;
        if (m < 0) m += 4;
        switch (m)
        {
            case 0: return "zero";
            case 1: return "one";
            case 2: return "two";
            default: return "three";
        }
    }

    // while loop, bounded so int.MinValue inputs don't hang the JS side.
    [Transpile, GenerateCrossTest(Samples = 12, Seed = 28)]
    public static int CountDownToZero(int n)
    {
        int i = n;
        if (i < 0) i = 0;
        if (i > 200) i = 200;
        int steps = 0;
        while (i > 0)
        {
            i--;
            steps++;
        }
        return steps;
    }

    // Integer-wrap corner cases for the existing `Total` method. Random
    // sampling almost never hits int.MaxValue * int.MinValue, but the
    // walker's Math.imul shape has to round-trip exactly when it does.
    [Transpile]
    [GenerateCrossTest(Samples = 6, Seed = 30)]
    [CrossTestCase(int.MaxValue, 1)]
    [CrossTestCase(int.MinValue, -1)]
    [CrossTestCase(int.MaxValue, int.MinValue)]
    [CrossTestCase(0, 0)]
    [CrossTestCase(-1, -1)]
    public static int WrapMul(int a, int b)
    {
        return a * b;
    }

    // Dictionary argument — sampler builds Dictionary<string,int> of 0..6
    // entries, the rule reads two keys. Cross-validates dictionary
    // shape + index access through JSON serialisation.
    [Transpile, GenerateCrossTest(Samples = 10, Seed = 31)]
    public static int CountTwoKeys(IReadOnlyDictionary<string, int> map, string a, string b)
    {
        int sum = 0;
        if (map.ContainsKey(a)) sum += map[a];
        if (map.ContainsKey(b)) sum += map[b];
        return sum;
    }

    // List mutation — build a list with Add() and read .Count. Tests the
    // walker mapping List.Add -> push and List.Count -> length end to end.
    [Transpile, GenerateCrossTest(Samples = 8, Seed = 32)]
    public static int BuildListAndCount(int n)
    {
        if (n < 0) n = 0;
        if (n > 50) n = 50;
        var xs = new List<int>();
        for (int i = 0; i < n; i++) xs.Add(i);
        return xs.Count;
    }

    // BigInt arithmetic — random long across the full 64-bit range,
    // BigInt.asIntN(64, ...) wrap on both sides so int.MaxValue * 2 etc.
    // round-trip identically.
    [Transpile, GenerateCrossTest(Samples = 12, Seed = 33)]
    [CrossTestCase(long.MinValue, 1L)]
    [CrossTestCase(long.MaxValue, 1L)]
    [CrossTestCase(0L, 0L)]
    public static long WrapAddLong(long a, long b) => a + b;

    [Transpile, GenerateCrossTest(Samples = 12, Seed = 34)]
    [CrossTestCase(long.MaxValue, 2L)]
    [CrossTestCase(long.MinValue, -1L)]
    public static long WrapMulLong(long a, long b) => a * b;

    // Math.Round (default banker's) — corner cases at the half-way points
    // where C# rounds to even but JS Math.round rounds half-away-from-zero.
    [Transpile]
    [GenerateCrossTest(Samples = 8, Seed = 35)]
    [CrossTestCase(0.5)]
    [CrossTestCase(1.5)]
    [CrossTestCase(2.5)]
    [CrossTestCase(-0.5)]
    [CrossTestCase(-1.5)]
    [CrossTestCase(-2.5)]
    public static double RoundEven(double x) => System.Math.Round(x);

    // Math.Round with MidpointRounding.AwayFromZero — diverges from JS too,
    // since JS Math.round rounds -0.5 toward +Inf (== 0), not -1.
    [Transpile]
    [GenerateCrossTest(Samples = 8, Seed = 36)]
    [CrossTestCase(0.5)]
    [CrossTestCase(-0.5)]
    [CrossTestCase(2.5)]
    [CrossTestCase(-2.5)]
    public static double RoundAway(double x) =>
        System.Math.Round(x, System.MidpointRounding.AwayFromZero);

    // Math.Truncate — semantically same as Math.trunc; sanity check the
    // whitelist mapping.
    [Transpile, GenerateCrossTest(Samples = 8, Seed = 37)]
    public static double TruncDouble(double x) => System.Math.Truncate(x);

    // do-while with break.
    [Transpile, GenerateCrossTest(Samples = 10, Seed = 29)]
    public static int FirstNonNegativeStep(int n)
    {
        int i = n;
        if (i < -50) i = -50;
        if (i > 50) i = 50;
        do
        {
            if (i >= 0) break;
            i++;
        } while (i < 100);
        return i;
    }

    // ---------------------------------------------------------------------
    // uint32. JS has no unsigned 32-bit type, so every one of these used to
    // diverge above 2^31 — and `UDiv` diverged at any value at all, since a
    // bare `a / b` is a float divide. Full-range uint sampling (see
    // FixtureGenerator) is what keeps these honest.
    // ---------------------------------------------------------------------

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 60)]
    [CrossTestCase(0u, 1u)]
    [CrossTestCase(uint.MaxValue, 1u)]
    [CrossTestCase(2147483648u, 2147483648u)]
    public static uint UAdd(uint a, uint b) => a + b;

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 61)]
    [CrossTestCase(0u, 1u)]
    [CrossTestCase(1u, 2u)]
    [CrossTestCase(uint.MaxValue, uint.MaxValue)]
    public static uint USub(uint a, uint b) => a - b;

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 62)]
    [CrossTestCase(65536u, 65536u)]
    [CrossTestCase(uint.MaxValue, 3u)]
    [CrossTestCase(2147483648u, 2u)]
    public static uint UMul(uint a, uint b) => a * b;

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 63)]
    [CrossTestCase(7u, 2u)]
    [CrossTestCase(uint.MaxValue, 7u)]
    [CrossTestCase(1u, 0u)]
    public static uint UDiv(uint a, uint b) => b == 0 ? 0u : a / b;

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 64)]
    [CrossTestCase(7u, 2u)]
    [CrossTestCase(uint.MaxValue, 7u)]
    [CrossTestCase(1u, 0u)]
    public static uint UMod(uint a, uint b) => b == 0 ? 0u : a % b;

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 65)]
    [CrossTestCase(1u, 31)]
    [CrossTestCase(2147483648u, 1)]
    public static uint UShl(uint a, int n) => a << n;

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 66)]
    [CrossTestCase(2147483648u, 17)]
    [CrossTestCase(uint.MaxValue, 17)]
    [CrossTestCase(uint.MaxValue, 0)]
    public static uint UShr(uint a, int n) => a >> n;

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 67)]
    [CrossTestCase(2147483648u, 2147483648u)]
    public static uint UAnd(uint a, uint b) => a & b;

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 68)]
    [CrossTestCase(2147483648u, 1u)]
    public static uint UOr(uint a, uint b) => a | b;

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 69)]
    [CrossTestCase(2147483648u, 2147483648u)]
    public static uint UXor(uint a, uint b) => a ^ b;

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 70)]
    [CrossTestCase(0u)]
    [CrossTestCase(uint.MaxValue)]
    public static uint UNot(uint a) => ~a;

    // Post-increment expands into a wrapped assignment; uint.MaxValue is the
    // case a bare `x++` gets wrong.
    [Transpile, GenerateCrossTest(Samples = 16, Seed = 71)]
    [CrossTestCase(uint.MaxValue)]
    [CrossTestCase(0u)]
    public static uint UIncrement(uint a)
    {
        uint x = a;
        x++;
        x++;
        return x;
    }

    // The shape that started this — textbook uint xorshift32, 64 rounds so any
    // single-bit divergence compounds into a completely different value.
    [Transpile, GenerateCrossTest(Samples = 24, Seed = 72)]
    [CrossTestCase(1u)]
    [CrossTestCase(uint.MaxValue)]
    [CrossTestCase(2147483648u)]
    public static uint UXorshift32(uint seed)
    {
        uint x = seed == 0u ? 1u : seed;
        for (int i = 0; i < 64; i++)
        {
            x = x ^ (x << 13);
            x = x ^ (x >> 17);
            x = x ^ (x << 5);
        }
        return x;
    }

    // ---------------------------------------------------------------------
    // Bitwise compound assignment and `>>>`. Both were build errors before,
    // so bit-twiddling code had to be written longhand.
    // ---------------------------------------------------------------------

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 80)]
    [CrossTestCase(int.MinValue, 0)]
    [CrossTestCase(-1, 0)]
    [CrossTestCase(-1, 1)]
    [CrossTestCase(int.MinValue, 31)]
    public static int IntUnsignedShift(int x, int n) => x >>> n;

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 81)]
    [CrossTestCase(uint.MaxValue, 0)]
    [CrossTestCase(2147483648u, 17)]
    public static uint UIntUnsignedShift(uint x, int n) => x >>> n;

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 82)]
    [CrossTestCase(int.MinValue, int.MaxValue)]
    public static int IntBitCompound(int a, int b)
    {
        int x = a;
        x ^= b;
        x &= b;
        x |= b;
        x <<= 3;
        x >>= 2;
        x >>>= 1;
        return x;
    }

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 83)]
    [CrossTestCase(uint.MaxValue, 2147483648u)]
    public static uint UIntBitCompound(uint a, uint b)
    {
        uint x = a;
        x ^= b;
        x &= b;
        x |= b;
        x <<= 3;
        x >>= 2;
        return x;
    }

    // Textbook xorshift32 — now writable with `^=` instead of longhand.
    [Transpile, GenerateCrossTest(Samples = 24, Seed = 84)]
    [CrossTestCase(1u)]
    [CrossTestCase(uint.MaxValue)]
    [CrossTestCase(2147483648u)]
    public static uint XorshiftCompound(uint seed)
    {
        uint x = seed == 0u ? 1u : seed;
        for (int i = 0; i < 64; i++)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
        }
        return x;
    }

    // C# masks 64-bit shift counts to their low 6 bits; JS BigInt does not.
    // Full-range int counts here — an unmasked emit both diverges and hangs
    // the JS side trying to build a multi-gigabit integer.
    [Transpile, GenerateCrossTest(Samples = 24, Seed = 85)]
    [CrossTestCase(1L, 0)]
    [CrossTestCase(1L, 63)]
    [CrossTestCase(1L, 64)]
    [CrossTestCase(1L, 100)]
    [CrossTestCase(-1L, 65)]
    public static long LongShiftCount(long a, int n) => a << n;

    [Transpile, GenerateCrossTest(Samples = 24, Seed = 86)]
    [CrossTestCase(1L, 64)]
    [CrossTestCase(-1L, 100)]
    public static long LongShiftCompound(long a, int n)
    {
        long x = a;
        x <<= n;
        x >>= n;
        x ^= a;
        return x;
    }
}
