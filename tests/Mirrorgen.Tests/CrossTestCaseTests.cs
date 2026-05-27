using Mirrorgen;
using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests
{

public class CrossTestCaseTests
{
    [Fact]
    public void Explicit_Case_Appears_Before_Random_Samples()
    {
        var methodInfo = typeof(SubjectMethods).GetMethod(nameof(SubjectMethods.Identity))!;
        var record = FixtureGenerator.GenerateFor(methodInfo);
        Assert.Equal(5, record.Calls.Count); // 2 explicit + 3 random
        Assert.Equal(42, record.Calls[0].Args[0]);
        Assert.Equal(42, record.Calls[0].Expected);
        Assert.Equal(-1, record.Calls[1].Args[0]);
        Assert.Equal(-1, record.Calls[1].Expected);
    }

    [Fact]
    public void Explicit_Case_Without_Samples_Still_Emits()
    {
        var methodInfo = typeof(SubjectMethods).GetMethod(nameof(SubjectMethods.ExplicitOnly))!;
        var record = FixtureGenerator.GenerateFor(methodInfo);
        Assert.Equal(2, record.Calls.Count);
        Assert.Equal(0, record.Calls[0].Args[0]);
        Assert.Equal(int.MaxValue, record.Calls[1].Args[0]);
    }

    [Fact]
    public void Wrong_Arg_Count_Throws()
    {
        var methodInfo = typeof(SubjectMethods).GetMethod(nameof(SubjectMethods.WrongArity))!;
        Assert.Throws<NotSupportedException>(() => FixtureGenerator.GenerateFor(methodInfo));
    }

    [Fact]
    public void No_Samples_No_Explicit_Throws()
    {
        var methodInfo = typeof(SubjectMethods).GetMethod(nameof(SubjectMethods.NeitherSamplesNorCases))!;
        Assert.Throws<NotSupportedException>(() => FixtureGenerator.GenerateFor(methodInfo));
    }
}

public static class SubjectMethods
{
    [Transpile]
    [GenerateCrossTest(Samples = 3, Seed = 1)]
    [CrossTestCase(42)]
    [CrossTestCase(-1)]
    public static int Identity(int x) => x;

    [Transpile]
    [GenerateCrossTest]
    [CrossTestCase(0)]
    [CrossTestCase(int.MaxValue)]
    public static int ExplicitOnly(int x) => x;

    [Transpile]
    [GenerateCrossTest(Samples = 1, Seed = 1)]
    [CrossTestCase(1, 2)] // method takes one arg
    public static int WrongArity(int x) => x;

    [Transpile]
    [GenerateCrossTest]
    public static int NeitherSamplesNorCases(int x) => x;
}

}
