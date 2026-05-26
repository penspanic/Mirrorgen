using System.Reflection;
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

    public static class Sampled
    {
        [Transpile, GenerateCrossTest(Samples = 5, Seed = 7)]
        public static int AddOne(int x) => x + 1;

        [Transpile, GenerateCrossTest(Samples = 3, Seed = 42)]
        public static bool Negate(bool b) => !b;

        [Transpile, GenerateCrossTest(Samples = 4, Seed = 99)]
        public static double DoubleIt(double v) => v * 2.0;
    }

    // Held in a separate type so assembly-wide GenerateForAssembly scans
    // don't trip over its intentionally-misconfigured method.
    static class Misconfigured
    {
        [Transpile, GenerateCrossTest]
        public static int NoSamples(int x) => x;
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
    public void Parameterized_Without_CrossTest_Attribute_Throws()
    {
        var m = typeof(Parameterized).GetMethod(nameof(Parameterized.WithArg))!;
        Assert.Throws<NotSupportedException>(() => FixtureGenerator.GenerateFor(m));
    }

    [Fact]
    public void Parameterized_Without_Samples_Throws()
    {
        var m = typeof(Misconfigured).GetMethod(nameof(Misconfigured.NoSamples), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        Assert.Throws<NotSupportedException>(() => FixtureGenerator.GenerateFor(m));
    }

    [Fact]
    public void Sampled_Method_Generates_N_Calls()
    {
        var m = typeof(Sampled).GetMethod(nameof(Sampled.AddOne))!;
        var record = FixtureGenerator.GenerateFor(m);
        Assert.Equal(5, record.Calls.Count);
        foreach (var call in record.Calls)
        {
            var x = Assert.IsType<int>(Assert.Single(call.Args));
            Assert.Equal(x + 1, call.Expected);
        }
    }

    [Fact]
    public void Sampled_Method_Is_Deterministic_For_Same_Seed()
    {
        var m = typeof(Sampled).GetMethod(nameof(Sampled.AddOne))!;
        var first = FixtureGenerator.GenerateFor(m);
        var second = FixtureGenerator.GenerateFor(m);
        for (int i = 0; i < first.Calls.Count; i++)
        {
            Assert.Equal(first.Calls[i].Args[0], second.Calls[i].Args[0]);
        }
    }

    [Fact]
    public void Sampled_Bool_Method_Invokes_Correctly()
    {
        var m = typeof(Sampled).GetMethod(nameof(Sampled.Negate))!;
        var record = FixtureGenerator.GenerateFor(m);
        Assert.Equal(3, record.Calls.Count);
        foreach (var call in record.Calls)
        {
            var b = Assert.IsType<bool>(Assert.Single(call.Args));
            Assert.Equal(!b, call.Expected);
        }
    }

    [Fact]
    public void Sampled_Double_Method_Stays_In_Range()
    {
        var m = typeof(Sampled).GetMethod(nameof(Sampled.DoubleIt))!;
        var record = FixtureGenerator.GenerateFor(m);
        Assert.Equal(4, record.Calls.Count);
        foreach (var call in record.Calls)
        {
            var v = Assert.IsType<double>(Assert.Single(call.Args));
            Assert.InRange(v, -100.0, 100.0);
            Assert.Equal(v * 2.0, (double)call.Expected!, precision: 12);
        }
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
