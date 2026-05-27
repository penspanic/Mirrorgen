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
}
