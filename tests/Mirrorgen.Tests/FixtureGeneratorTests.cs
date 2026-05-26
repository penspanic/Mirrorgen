using Mirrorgen;
using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class FixtureGeneratorTests
{
    public static class Subject
    {
        [Transpile, GenerateCrossTest]
        public static int FortyTwo() => 42;

        [Transpile, GenerateCrossTest]
        public static bool AlwaysTrue() => true;

        [Transpile, GenerateCrossTest]
        public static string Hello() => "hi";

        [Transpile]
        public static int OnlyTranspile() => 0;

        [GenerateCrossTest]
        public static int OnlyCrossTest() => 0;

        public static int Bare() => 0;
    }

    public static class Parameterized
    {
        public static int WithArg(int x) => x + 1;
    }

    [Fact]
    public void Generates_Records_Only_For_Methods_With_Both_Attributes()
    {
        var records = FixtureGenerator.GenerateForAssembly(typeof(Subject).Assembly);
        var names = records.Select(r => r.Name).ToHashSet();

        Assert.Contains("FortyTwo", names);
        Assert.Contains("AlwaysTrue", names);
        Assert.Contains("Hello", names);
        Assert.DoesNotContain("OnlyTranspile", names);
        Assert.DoesNotContain("OnlyCrossTest", names);
        Assert.DoesNotContain("Bare", names);
    }

    [Fact]
    public void Captures_Int_Return_Value()
    {
        var record = FixtureGenerator.GenerateFor(typeof(Subject).GetMethod(nameof(Subject.FortyTwo))!);
        var call = Assert.Single(record.Calls);
        Assert.Equal(42, call.Expected);
        Assert.Empty(call.Args);
    }

    [Fact]
    public void Captures_Bool_Return_Value()
    {
        var record = FixtureGenerator.GenerateFor(typeof(Subject).GetMethod(nameof(Subject.AlwaysTrue))!);
        Assert.Equal(true, record.Calls[0].Expected);
    }

    [Fact]
    public void Captures_String_Return_Value()
    {
        var record = FixtureGenerator.GenerateFor(typeof(Subject).GetMethod(nameof(Subject.Hello))!);
        Assert.Equal("hi", record.Calls[0].Expected);
    }

    [Fact]
    public void Parameterized_Method_NotSupported_In_V0()
    {
        var m = typeof(Parameterized).GetMethod(nameof(Parameterized.WithArg))!;
        Assert.Throws<NotSupportedException>(() => FixtureGenerator.GenerateFor(m));
    }

    [Fact]
    public void Json_Has_Expected_Shape()
    {
        var records = FixtureGenerator.GenerateForAssembly(typeof(Subject).Assembly);
        var fortyTwo = records.Single(r => r.Name == "FortyTwo");
        var json = FixtureGenerator.SerializeToJson(new[] { fortyTwo });

        Assert.Contains("\"name\": \"FortyTwo\"", json);
        Assert.Contains("\"expected\": 42", json);
        Assert.Contains("\"args\": []", json);
    }
}
