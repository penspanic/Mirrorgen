# Mirrorgen — Subset spec

**English** · [한국어](SUBSET_ko.md)

This document is the precise spec of what the v0.1 transpiler accepts. Anything not listed here is unsupported and either fails at emit time (`NotSupportedException`) or is rejected earlier by the analyzers in `Mirrorgen.Analyzers`.

The subset is intentionally narrow so the output stays predictable, debuggable, and byte-exact across the C# / TypeScript boundary.

The high-level rationale lives in [`CONCEPT.md`](CONCEPT.md); this file is the enumeration.

## Type surface (v0.1)

Supported:
- `enum` (int-backed)
- `record` (positional or property-init)
- `class` and `struct` with only get-only / init-only properties
- Primitive types: `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long` (BigInt), `float`, `double`, `string`
- `T[]`, `List<T>`, `IReadOnlyList<T>` (all emitted as `T[]`)
- `Dictionary<K,V>`, `IReadOnlyDictionary<K,V>` (emitted as `Record<K,V>` with string-coercible K)
- `Nullable<T>` (emitted as `T | null`)
- Transitive reachability — leaf types reached from a marked type don't need their own attribute

Not supported in v0.1:
- Mutable collections beyond construction
- `Tuple<...>` / `ValueTuple` — encourage records instead
- Custom generic types defined by user code (deferred to v0.3)
- Inheritance / interface implementation surface

## Expression / statement surface (v0.1)

Supported:
- Local variables (`var`, `int`, `bool`, ...)
- Arithmetic on integer types with correct wrap semantics:
  - `int` → wrapped with `| 0`
  - `uint` → wrapped with `>>> 0`
  - `short` / `ushort` / `byte` / `sbyte` → wrapped with explicit mask
  - Multiplication on int / uint → `Math.imul` (avoids JS Number precision loss)
- Boolean operators and comparisons
- `if` / `else`
- `for` (C-style), `foreach` (over `T[]` only in v0.1)
- `switch` statement and `switch` expression (constant patterns and type patterns over enums)
- Method calls to other `[Transpile]` methods within the same project
- Method calls to a whitelisted set of `System.Math.*` / `System.MathF.*` functions
- `return`

Not supported in v0.1:
- `while`, `do-while` (admitted in v0.2 once we're sure we want unbounded loops in generated code)
- LINQ in any form
- `goto`
- `yield return`
- `using`, `try` / `catch` / `finally`
- Pattern matching beyond enum constants
- Operator overloading on user types
- Generic methods

## Analyzer enforcement

Subset violations on `[Transpile]` members are surfaced as build errors with stable diagnostic ids:

| Id      | Severity | What it flags                                                                           |
|---------|----------|------------------------------------------------------------------------------------------|
| MG0001  | Error    | LINQ (any `System.Linq` invocation)                                                      |
| MG0002  | Error    | `async` / `await` / `Task` / `ValueTask`                                                 |
| MG0003  | Error    | `Span<T>` / `ReadOnlySpan<T>` / `ref` / `in` / `out` / `unsafe` / pointer types          |
| MG0004  | Error    | `throw`                                                                                  |
| MG0005  | Error    | `System.Reflection.*`, `System.Type` / `System.Activator` calls, `typeof(...)`           |
| MG0006  | Error    | declaring class of a `[Transpile]` method inherits from a non-`object` base              |

Diagnostic ids in the `MG0001`..`MG0099` range are stable. Suppressing them with `#pragma warning disable` is supported but not recommended — Mirrorgen makes no guarantee about emit correctness for suppressed cases.
