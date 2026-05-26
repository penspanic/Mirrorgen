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
}
