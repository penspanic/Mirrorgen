using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class TypeDeclTests
{
    [Fact]
    public void Positional_Record_Emits_Interface()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public record OrderLine(int Quantity, int UnitPrice, string Sku);
            """);
        Assert.Contains("export interface OrderLine {", ts);
        Assert.Contains("  Quantity: number;", ts);
        Assert.Contains("  UnitPrice: number;", ts);
        Assert.Contains("  Sku: string;", ts);
    }

    [Fact]
    public void Class_With_AutoProperties_Emits_Interface()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public class Cart
            {
                public int Subtotal { get; set; }
                public string CustomerName { get; init; } = "";
            }
            """);
        Assert.Contains("export interface Cart {", ts);
        Assert.Contains("  Subtotal: number;", ts);
        Assert.Contains("  CustomerName: string;", ts);
    }

    [Fact]
    public void Struct_With_Properties_Emits_Interface()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public struct Money
            {
                public int Cents { get; init; }
            }
            """);
        Assert.Contains("export interface Money {", ts);
        Assert.Contains("  Cents: number;", ts);
    }

    [Fact]
    public void Class_With_Public_Fields_Emits_Members()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public class Point
            {
                public int X;
                public int Y;
            }
            """);
        Assert.Contains("  X: number;", ts);
        Assert.Contains("  Y: number;", ts);
    }

    [Fact]
    public void Type_With_Array_Property_Uses_Array_Suffix()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public class Cart
            {
                public int[] LineIds { get; set; } = new int[0];
            }
            """);
        Assert.Contains("  LineIds: number[];", ts);
    }

    [Fact]
    public void Type_With_Reference_To_Another_Type_Uses_Identifier_Name()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public record OrderLine(int Quantity);

            [Mirrorgen.Transpile]
            public class Cart
            {
                public OrderLine[] Lines { get; set; } = new OrderLine[0];
            }
            """);
        // Both types should emit
        Assert.Contains("export interface OrderLine {", ts);
        Assert.Contains("export interface Cart {", ts);
        // Cart.Lines should reference OrderLine
        Assert.Contains("  Lines: OrderLine[];", ts);
    }

    [Fact]
    public void Record_Body_Adds_To_Positional_Members()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public record Person(string Name)
            {
                public int Age { get; init; }
            }
            """);
        Assert.Contains("  Name: string;", ts);
        Assert.Contains("  Age: number;", ts);
    }

    [Fact]
    public void Type_EmitName_Renames_Output()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile(EmitName = "OrderLine")]
            public record CsOrderLine(int Quantity);
            """);
        Assert.Contains("export interface OrderLine {", ts);
        Assert.DoesNotContain("CsOrderLine", ts);
    }
}
