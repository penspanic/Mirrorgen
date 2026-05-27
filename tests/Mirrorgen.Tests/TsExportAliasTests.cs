using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class TsExportAliasTests
{
    [Fact]
    public void TsExport_On_Record_Emits_Interface()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [TsExport]
            public record Foo(int X);
            """);
        Assert.Contains("export interface Foo {", ts);
        Assert.Contains("X: number;", ts);
    }

    [Fact]
    public void TsExportAttribute_Suffix_Recognised()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [TsExportAttribute]
            public record Foo(int X);
            """);
        Assert.Contains("export interface Foo {", ts);
    }

    [Fact]
    public void Qualified_TsExport_Recognised()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [OpenFieldFramework.Common.TsExport]
            public record Foo(int X);
            """);
        Assert.Contains("export interface Foo {", ts);
    }

    [Fact]
    public void TsExport_On_Class_Is_Shape_Only_No_Method_Emit()
    {
        // [TsExport] is intentionally shape-only — methods don't auto-emit.
        var ts = TranspilerEngine.TranspileSource("""
            [TsExport]
            public static class K {
                public const int Magic = 42;
                public static int F(int x) => x;
            }
            """);
        Assert.Contains("export const Magic: number = 42;", ts);
        Assert.DoesNotContain("export function F", ts);
    }

    [Fact]
    public void Mixed_TsExport_And_Transpile_Method_Emit_Still_Works()
    {
        // Adding [Transpile] on a method inside a [TsExport] class still emits
        // the method explicitly — caller chose to opt in per-method.
        var ts = TranspilerEngine.TranspileSource("""
            [TsExport]
            public static class K {
                public const int Magic = 42;
                [Mirrorgen.Attributes.Transpile]
                public static int F(int x) => x;
            }
            """);
        Assert.Contains("export const Magic: number = 42;", ts);
        Assert.Contains("export function F", ts);
    }

    [Fact]
    public void TsExport_On_Enum_Emits()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [TsExport]
            public enum Color { Red, Green, Blue }
            """);
        Assert.Contains("export enum Color {", ts);
        Assert.Contains("Red = 0,", ts);
    }
}
