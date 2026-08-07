using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class ReachabilityTests
{
    [Fact]
    public void Transpile_Type_Pulls_In_Referenced_Type_Without_Attribute()
    {
        // OrderLine has NO [Transpile] but is referenced by Cart's array property,
        // so the reachability scan should pull it in.
        var ts = TranspilerEngine.TranspileSource("""
            public record OrderLine(int Quantity);

            [Mirrorgen.Transpile]
            public class Cart
            {
                public OrderLine[] Lines { get; set; } = new OrderLine[0];
            }
            """);
        Assert.Contains("export interface OrderLine {", ts);
        Assert.Contains("export interface Cart {", ts);
    }

    [Fact]
    public void Transpile_Type_Pulls_In_Enum_Reference()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public enum DiscountKind { None, Flat, Pct }

            [Mirrorgen.Transpile]
            public record Discount(DiscountKind Kind, int Amount);
            """);
        Assert.Contains("export enum DiscountKind {", ts);
        Assert.Contains("export interface Discount {", ts);
    }

    [Fact]
    public void Transpile_Method_Pulls_In_Parameter_Type()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public record OrderLine(int Quantity);

            public static class S
            {
                [Mirrorgen.Transpile]
                public static int CountUnits(OrderLine line) => line.Quantity;
            }
            """);
        Assert.Contains("export interface OrderLine {", ts);
        Assert.Contains("export function CountUnits(line: OrderLine): number", ts);
    }

    [Fact]
    public void Unreferenced_Type_Without_Transpile_Is_Skipped()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public record Unused(int X);

            [Mirrorgen.Transpile]
            public record Used(int Y);
            """);
        Assert.Contains("export interface Used", ts);
        Assert.DoesNotContain("export interface Unused", ts);
    }

    [Fact]
    public void Reachability_Is_Transitive()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public record Leaf(int Value);
            public record Branch(Leaf[] Leaves);

            [Mirrorgen.Transpile]
            public record Root(Branch Branch);
            """);
        Assert.Contains("export interface Root", ts);
        Assert.Contains("export interface Branch", ts);
        Assert.Contains("export interface Leaf", ts);
    }
}
