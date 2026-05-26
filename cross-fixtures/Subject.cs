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
}
