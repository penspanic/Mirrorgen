using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class ValidatorEmitTests
{
    static readonly TranspileOptions WithValidators = new() { EmitValidators = true };

    [Fact]
    public void Default_Options_Do_Not_Emit_Validators()
    {
        var ts = TranspilerEngine.TranspileSource("""
            [Mirrorgen.Transpile]
            public record Foo(int X);
            """);
        Assert.DoesNotContain("parseFoo", ts);
    }

    [Fact]
    public void Validator_Emitted_For_Each_Interface()
    {
        var ts = TranspilerEngine.TranspileSource(
            """
            [Mirrorgen.Transpile]
            public record Foo(int X, string Name);
            """,
            WithValidators);
        Assert.Contains("export function parseFoo(value: unknown): Foo {", ts);
        Assert.Contains("if (typeof value !== 'object' || value === null)", ts);
        Assert.Contains("if (typeof x !== 'number')", ts);
        Assert.Contains("if (typeof x !== 'string')", ts);
    }

    [Fact]
    public void Nullable_Field_Validator_Tolerates_Null_And_Undefined()
    {
        var ts = TranspilerEngine.TranspileSource(
            """
            [Mirrorgen.Transpile]
            public record Foo(int? Maybe);
            """,
            WithValidators);
        Assert.Contains("if (x !== null && x !== undefined)", ts);
        Assert.DoesNotContain("if (x === undefined) throw new TypeError(`Foo.Maybe", ts);
    }

    [Fact]
    public void Nullable_Field_Validator_Normalizes_Omitted_To_Null()
    {
        // Wire documents may omit a nullable field; the interface contract is
        // "always present, value may be null" — the parse step bridges the two.
        var ts = TranspilerEngine.TranspileSource(
            """
            [Mirrorgen.Transpile]
            public record Foo(int? Maybe);
            """,
            WithValidators);
        Assert.Contains("} else if (x === undefined) {", ts);
        Assert.Contains("o[\"Maybe\"] = null;", ts);
    }

    [Fact]
    public void Required_Field_Throws_On_Undefined()
    {
        var ts = TranspilerEngine.TranspileSource(
            """
            [Mirrorgen.Transpile]
            public record Foo(int Required);
            """,
            WithValidators);
        Assert.Contains("if (x === undefined) throw new TypeError(`Foo.Required: required`);", ts);
    }

    [Fact]
    public void Array_Field_Validator_Uses_Array_isArray()
    {
        var ts = TranspilerEngine.TranspileSource(
            """
            [Mirrorgen.Transpile]
            public class Foo {
                public System.Collections.Generic.List<int> Items { get; init; }
            }
            """,
            WithValidators);
        Assert.Contains("if (!Array.isArray(x))", ts);
    }

    [Fact]
    public void Dictionary_Field_Validator_Checks_Object()
    {
        var ts = TranspilerEngine.TranspileSource(
            """
            [Mirrorgen.Transpile]
            public class Foo {
                public System.Collections.Generic.Dictionary<string, int> Map { get; init; }
            }
            """,
            WithValidators);
        // Record<K,V> branch
        Assert.Contains("if (typeof x !== 'object' || x === null)", ts);
    }

    [Fact]
    public void Nested_Interface_Validator_Calls_Parse_Transitively()
    {
        var ts = TranspilerEngine.TranspileSource(
            """
            [Mirrorgen.Transpile]
            public record Inner(int N);
            [Mirrorgen.Transpile]
            public record Outer(Inner Child);
            """,
            WithValidators);
        Assert.Contains("export function parseInner(value: unknown): Inner {", ts);
        Assert.Contains("export function parseOuter(value: unknown): Outer {", ts);
        // Outer's body calls parseInner on the child field
        var outerStart = ts.IndexOf("export function parseOuter");
        Assert.True(outerStart >= 0);
        var outerBody = ts.Substring(outerStart);
        Assert.Contains("parseInner(x);", outerBody);
    }

    [Fact]
    public void Enum_Field_Validator_Accepts_Number_Or_String()
    {
        var ts = TranspilerEngine.TranspileSource(
            """
            [Mirrorgen.Transpile]
            public enum Kind { A, B }
            [Mirrorgen.Transpile]
            public record Foo(Kind Tag);
            """,
            WithValidators);
        Assert.Contains("if (typeof x !== 'number' && typeof x !== 'string')", ts);
    }
}
