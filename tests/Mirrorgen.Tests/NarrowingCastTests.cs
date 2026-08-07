using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class NarrowingCastTests
{
    [Fact]
    public void Byte_Cast_From_Int_Masks_With_0xff()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class K {
                [Mirrorgen.Transpile] public static byte F(int x) => (byte)x;
            }
            """);
        Assert.Contains("((x) & 0xff)", ts);
    }

    [Fact]
    public void SByte_Cast_From_Int_Sign_Extends()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class K {
                [Mirrorgen.Transpile] public static sbyte F(int x) => (sbyte)x;
            }
            """);
        Assert.Contains("(((x) << 24) >> 24)", ts);
    }

    [Fact]
    public void Short_Cast_From_Int_Sign_Extends_16()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class K {
                [Mirrorgen.Transpile] public static short F(int x) => (short)x;
            }
            """);
        Assert.Contains("(((x) << 16) >> 16)", ts);
    }

    [Fact]
    public void UShort_Cast_From_Int_Masks_16()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class K {
                [Mirrorgen.Transpile] public static ushort F(int x) => (ushort)x;
            }
            """);
        Assert.Contains("((x) & 0xffff)", ts);
    }

    [Fact]
    public void Byte_Cast_Of_Binary_Expression_Preserves_Inner_Wrap()
    {
        // (byte)(FirstSandTileId + groupId) — the inner expression must keep
        // emitting whatever it does normally, with the cast wrapping it.
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public static class K {
                public const byte FirstSandTileId = 2;
                [Mirrorgen.Transpile] public static byte SandTileId(byte groupId) => (byte)(FirstSandTileId + groupId);
            }
            """);
        Assert.Contains("export const FirstSandTileId: number = 2;", ts);
        // Inner is parenthesized binary; cast adds the mask.
        Assert.Contains("& 0xff", ts);
    }

    [Fact]
    public void Byte_Cast_From_BigInt_Uses_BigInt_Mask()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class K {
                [Mirrorgen.Transpile] public static byte F(long x) => (byte)x;
            }
            """);
        Assert.Contains("Number(x & 0xffn)", ts);
    }

    [Fact]
    public void UShort_Cast_From_BigInt_Uses_BigInt_Mask()
    {
        var ts = TranspilerEngine.TranspileSource("""
            public static class K {
                [Mirrorgen.Transpile] public static ushort F(ulong x) => (ushort)x;
            }
            """);
        Assert.Contains("Number(x & 0xffffn)", ts);
    }
}
