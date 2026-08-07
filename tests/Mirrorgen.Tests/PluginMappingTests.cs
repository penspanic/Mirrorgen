using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests
{

public class PluginMappingTests
{
    static TypeMappingRegistry BuildRegistry(System.Action<MirrorgenBuilder> configure)
    {
        var b = new MirrorgenBuilder();
        configure(b);
        return b.Build();
    }

    [Fact]
    public void Mapped_Wrapper_Type_Emits_As_Primitive_In_Method_Signature()
    {
        var registry = BuildRegistry(b =>
            b.MapType(typeof(App.OrderId)).AsPrimitive("number"));

        var ts = TranspilerEngine.TranspileSource("""
            using App;
            public static class S
            {
                [Mirrorgen.Transpile]
                public static int Inc(OrderId id) => 0;
            }
            """, registry);
        Assert.Contains("Inc(id: number): number", ts);
    }

    [Fact]
    public void Mapped_Wrapper_Type_Emits_As_Property_Type()
    {
        var registry = BuildRegistry(b =>
            b.MapType(typeof(App.OrderId)).AsPrimitive("number"));

        var ts = TranspilerEngine.TranspileSource("""
            using App;
            [Mirrorgen.Transpile]
            public record Cart(OrderId Id, int Total);
            """, registry);
        Assert.Contains("  Id: number;", ts);
        Assert.Contains("  Total: number;", ts);
    }

    [Fact]
    public void Mapped_Type_Declaration_Is_Not_Emitted_Even_If_Marked_Transpile()
    {
        // OrderId is mapped to number, so even with [Transpile] the
        // declaration must not also emit — that would shadow the mapping.
        var registry = BuildRegistry(b =>
            b.MapType(typeof(App.OrderId)).AsPrimitive("number"));

        var ts = TranspilerEngine.TranspileSource("""
            using App;
            namespace App {
                [Mirrorgen.Transpile]
                public readonly record struct OrderId(int Value);
            }
            [Mirrorgen.Transpile]
            public record Cart(App.OrderId Id);
            """, registry);
        Assert.DoesNotContain("export interface OrderId", ts);
        Assert.Contains("  Id: number;", ts);
    }

    [Fact]
    public void RuntimeImport_Uses_Declared_Name_Verbatim()
    {
        var registry = BuildRegistry(b =>
            b.MapType(typeof(App.Money)).RuntimeImport("Money"));

        var ts = TranspilerEngine.TranspileSource("""
            using App;
            [Mirrorgen.Transpile]
            public record Line(Money Amount);
            """, registry);
        Assert.Contains("  Amount: Money;", ts);
    }

    [Fact]
    public void Duplicate_MapType_Call_Throws()
    {
        var b = new MirrorgenBuilder();
        b.MapType(typeof(App.OrderId)).AsPrimitive("number");
        Assert.Throws<System.InvalidOperationException>(() =>
            b.MapType(typeof(App.OrderId)).AsPrimitive("string"));
    }

    [Fact]
    public void Empty_Registry_Preserves_Default_Behaviour()
    {
        // The mapping path must be a strict superset of the no-registry
        // path: same input → same output when nothing is registered.
        var ts1 = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public record Order(int Id);
            """);
        var ts2 = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public record Order(int Id);
            """, TypeMappingRegistry.Empty);
        Assert.Equal(ts1, ts2);
    }
}

}

// Stand-in domain types referenced by the registry-driven tests above.
namespace App
{
    public readonly record struct OrderId(int Value);
    public readonly record struct Money(int Cents);
}
