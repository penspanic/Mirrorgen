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

    [Fact]
    public void Field_EmitName_Overrides_Const_Name_And_References()
    {
        // Two [Transpile] classes can collide on a common const name when
        // both surface to the same .ts module (e.g. HilbertCurve.MaxLevel
        // and CubeSphereCellId.MaxLevel). `[Transpile(EmitName="…")]` on the
        // field renames *both* the `export const` and every identifier
        // reference inside transpiled bodies so the override flows through.
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            [Mirrorgen.Transpile]
            public static class S {
                [Mirrorgen.Transpile(EmitName = "MaxCellLevel")]
                public const int MaxLevel = 26;

                public static bool TooDeep(int level) => level > MaxLevel;
            }
            """);
        Assert.Contains("export const MaxCellLevel: number = 26;", ts);
        Assert.Contains("return level > MaxCellLevel;", ts);
        // The original name must NOT leak into the emit — that would imply
        // an undefined reference at the call site.
        Assert.DoesNotContain("MaxLevel", ts.Replace("MaxCellLevel", ""));
    }

    [Fact]
    public void Cast_To_User_Enum_Emits_TS_As_Keyword()
    {
        // `(CubeFace)x` in TS strict mode needs `as CubeFace` so the
        // assignment to a `CubeFace`-typed local type-checks. Without it,
        // emit would produce `let f: CubeFace = (x)` which strict mode
        // rejects (number → enum is not implicit).
        var ts = TranspilerEngine.TranspileSource("""
            using System;
            public enum CubeFace : byte { PosX = 0, NegX = 1 }
            public static class S {
                [Mirrorgen.Transpile]
                public static CubeFace FaceOf(int raw) {
                    return (CubeFace)raw;
                }
            }
            """);
        Assert.Contains("as CubeFace", ts);
    }
}
