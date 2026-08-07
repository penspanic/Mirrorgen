using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class GenericMethodTests
{
    [Fact]
    public void Single_Type_Parameter_Identity_Roundtrips()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static T Identity<T>(T x) => x;
            }
            """);
        Assert.Contains("export function Identity<T>(x: T): T {", ts);
        Assert.Contains("return x;", ts);
    }

    [Fact]
    public void Two_Type_Parameters_Both_Emit()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static T Pick<T, U>(T a, U b) => a;
            }
            """);
        Assert.Contains("export function Pick<T, U>(a: T, b: U): T {", ts);
    }

    [Fact]
    public void Generic_T_In_Array_Position()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static T First<T>(T[] xs) => xs[0];
            }
            """);
        Assert.Contains("export function First<T>(xs: T[]): T {", ts);
        // Element reads carry a non-null assertion so the emit still typechecks
        // under `noUncheckedIndexedAccess`, where `xs[0]` is `T | undefined`.
        Assert.Contains("return xs[0]!;", ts);
    }

    [Fact]
    public void Generic_Method_With_Constraint_Throws()
    {
        Assert.Throws<System.NotSupportedException>(() =>
            TranspilerEngine.TranspileSource("""
                public static class S {
                    [Mirrorgen.Transpile]
                    public static T Default<T>() where T : new() => new T();
                }
                """));
    }
}
