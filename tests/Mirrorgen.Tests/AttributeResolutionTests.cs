using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

/// <summary>
/// `[Transpile]` recognition resolves the attribute symbol rather than matching
/// its name. A name-only test cannot tell Mirrorgen's attribute from a
/// same-named one in another namespace, and it accepts namespaces that do not
/// exist — which is how the same attribute came to mean different things
/// depending on whether it sat on a method or on a type.
/// </summary>
public class AttributeResolutionTests
{
    [Fact]
    public void Qualified_And_Short_Forms_Both_Resolve()
    {
        var qualified = TranspilerEngine.TranspileSource("""
            public static class S {
                [Mirrorgen.Transpile]
                public static int A() => 1;
            }
            """);
        Assert.Contains("export function A(): number", qualified);

        var shortForm = TranspilerEngine.TranspileSource("""
            using Mirrorgen;
            public static class S {
                [Transpile]
                public static int A() => 1;
            }
            """);
        Assert.Contains("export function A(): number", shortForm);
    }

    [Fact]
    public void Alias_Qualified_Form_Resolves()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using MG = Mirrorgen;
            public static class S {
                [MG.Transpile]
                public static int A() => 1;
            }
            """);
        Assert.Contains("export function A(): number", ts);
    }

    [Fact]
    public void Method_And_Type_Level_Agree_On_A_Nonexistent_Namespace()
    {
        // The reported inconsistency: `Mirrorgen.Attributes` is the *assembly*
        // name, not a namespace. Method-level recognition used to accept it
        // (name ends with ".Transpile") while type-level recognition resolved
        // the symbol and rejected it — so the class emitted as a bare shape and
        // the first `new` of it failed with an unrelated "Unsupported `new`
        // expression" error. Both paths now reject it, and say why.
        var onMethod = Assert.Throws<System.NotSupportedException>(() =>
            TranspilerEngine.TranspileSource("""
                public static class S {
                    [Mirrorgen.Attributes.Transpile]
                    public static int A() => 1;
                }
                """));
        Assert.Contains("does not resolve to a type", onMethod.Message);
        Assert.Contains("Mirrorgen.Attributes", onMethod.Message);

        var onType = Assert.Throws<System.NotSupportedException>(() =>
            TranspilerEngine.TranspileSource("""
                [Mirrorgen.Attributes.Transpile]
                public sealed class Proj {
                    public static readonly Proj Instance = new();
                    public int P(int x) => x;
                }
                """));
        Assert.Contains("does not resolve to a type", onType.Message);
    }

    [Fact]
    public void A_Same_Named_Attribute_From_Another_Namespace_Is_Not_Mirrorgens()
    {
        // Resolves fine — it just is not Mirrorgen's attribute, so nothing
        // should be emitted for it.
        var ts = TranspilerEngine.TranspileSource("""
            namespace Other { public sealed class TranspileAttribute : System.Attribute { } }

            public static class S {
                [Other.Transpile]
                public static int A() => 1;
            }
            """);
        Assert.DoesNotContain("export function A", ts);
    }
}
