using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class RecordConstructionTests
{
    const string CellIdShape = """
        [Mirrorgen.Transpile]
        public readonly partial record struct CellId(ulong High, ulong Low);
        """;

    [Fact]
    public void New_Positional_Record_Becomes_Object_Literal()
    {
        var ts = TranspilerEngine.TranspileSource($$"""
            using System;
            {{CellIdShape}}
            public static class S {
                [Mirrorgen.Transpile]
                public static CellId Make(ulong h, ulong l) {
                    return new CellId(h, l);
                }
            }
            """);
        Assert.Contains("return { High: h, Low: l };", ts);
    }

    [Fact]
    public void Target_Typed_New_Record_Also_Works()
    {
        var ts = TranspilerEngine.TranspileSource($$"""
            using System;
            {{CellIdShape}}
            public static class S {
                [Mirrorgen.Transpile]
                public static CellId Make(ulong h, ulong l) {
                    CellId c = new(h, l);
                    return c;
                }
            }
            """);
        Assert.Contains("let c: CellId = { High: h, Low: l };", ts);
    }

    [Fact]
    public void Array_Initializer_Emits_TS_Array_Literal()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            public static class S {
                [Mirrorgen.Transpile]
                public static int[] Triple(int a, int b, int c) {
                    return new[] { a, b, c };
                }
            }
            """);
        Assert.Contains("return [a, b, c];", ts);
    }

    [Fact]
    public void Static_Method_On_Record_Struct_Emits_As_Free_Function()
    {
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            // Type-level [Transpile] on the record-struct also seeds its
            // public static methods (mirrors class-level behaviour). The
            // positional record parameters become the interface shape; the
            // static helper emits as a free function at module scope.
            [Mirrorgen.Transpile]
            public readonly partial record struct CellId(ulong High, ulong Low)
            {
                public static CellId FromHL(ulong h, ulong l) => new CellId(h, l);
            }
            """);
        Assert.Contains("export function FromHL", ts);
    }

    [Fact]
    public void Computed_Property_Is_Skipped_From_Interface()
    {
        // Expression-bodied get-only properties (=> expr) and get-with-body
        // are *behaviour*, not storage shape. Letting them surface as fields
        // would imply setters and bloat the wire shape.
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            [Mirrorgen.Transpile]
            public readonly partial record struct Tag(int Id)
            {
                public bool IsZero => Id == 0;
            }
            """);
        Assert.Contains("export interface Tag", ts);
        Assert.Contains("Id: number;", ts);
        Assert.DoesNotContain("IsZero", ts);
    }
}
