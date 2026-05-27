using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class ConstFieldTests
{
    [Fact]
    public void Public_Const_Byte_Emits_Export_Const_With_Value()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public static class Encoding {
                public const byte AirTileId = 0;
            }
            """);
        Assert.Contains("export const AirTileId: number = 0;", ts);
        Assert.DoesNotContain("AirTileId: number;", ts);
    }

    [Fact]
    public void Const_Only_Class_Skips_Empty_Interface_Block()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public static class Encoding {
                public const byte AirTileId = 0;
                public const byte BedrockTileId = 1;
            }
            """);
        Assert.Contains("export const AirTileId: number = 0;", ts);
        Assert.Contains("export const BedrockTileId: number = 1;", ts);
        Assert.DoesNotContain("export interface Encoding", ts);
    }

    [Fact]
    public void Constants_For_All_Primitive_Types()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public static class K {
                public const sbyte A = -1;
                public const short B = -100;
                public const ushort C = 1000;
                public const int D = 42;
                public const uint E = 7u;
                public const float F = 1.5f;
                public const double G = 3.14;
                public const bool H = true;
                public const string I = "hi";
            }
            """);
        Assert.Contains("export const A: number = -1;", ts);
        Assert.Contains("export const B: number = -100;", ts);
        Assert.Contains("export const C: number = 1000;", ts);
        Assert.Contains("export const D: number = 42;", ts);
        Assert.Contains("export const E: number = 7;", ts);
        Assert.Contains("export const F: number = 1.5;", ts);
        Assert.Contains("export const G: number = 3.14;", ts);
        Assert.Contains("export const H: boolean = true;", ts);
        Assert.Contains("export const I: string = \"hi\";", ts);
    }

    [Fact]
    public void Long_And_Ulong_Const_Emit_As_BigInt_Literal()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public static class L {
                public const long A = 9000000000L;
                public const ulong B = 18000000000UL;
            }
            """);
        Assert.Contains("export const A: bigint = 9000000000n;", ts);
        Assert.Contains("export const B: bigint = 18000000000n;", ts);
    }

    [Fact]
    public void Constant_Expression_Initializer_Is_Evaluated()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public static class K {
                public const int Scale = 1 << 8;
                public const int HalfScale = Scale / 2;
                public const byte FlagAB = (1 << 0) | (1 << 1);
            }
            """);
        Assert.Contains("export const Scale: number = 256;", ts);
        Assert.Contains("export const HalfScale: number = 128;", ts);
        Assert.Contains("export const FlagAB: number = 3;", ts);
    }

    [Fact]
    public void Non_Public_Const_Is_Ignored()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public static class K {
                public const int Public = 1;
                internal const int Internal = 2;
                private const int Private = 3;
            }
            """);
        Assert.Contains("export const Public: number = 1;", ts);
        Assert.DoesNotContain("Internal", ts);
        Assert.DoesNotContain("Private", ts);
    }

    [Fact]
    public void String_Const_Escapes_Special_Characters()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public static class K {
                public const string A = "line1\nline2";
                public const string B = "with \"quotes\"";
                public const string C = "back\\slash";
            }
            """);
        Assert.Contains(@"export const A: string = ""line1\nline2"";", ts);
        Assert.Contains(@"export const B: string = ""with \""quotes\"""";", ts);
        Assert.Contains(@"export const C: string = ""back\\slash"";", ts);
    }

    [Fact]
    public void Existing_Interface_Emit_Still_Works_When_Mixed_With_Const()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Attributes.Transpile]
            public class Mixed {
                public const int Version = 1;
                public int Value { get; init; }
            }
            """);
        Assert.Contains("export const Version: number = 1;", ts);
        Assert.Contains("export interface Mixed {", ts);
        Assert.Contains("Value: number;", ts);
    }
}
