# Mirrorgen — Subset spec

**English** · [한국어](SUBSET_ko.md)

This document is the precise spec of what the v0.1 transpiler accepts. Anything not listed here is unsupported and either fails at emit time (`NotSupportedException`) or is rejected earlier by the analyzers in `Mirrorgen.Analyzers`.

The subset is intentionally narrow so the output stays predictable, debuggable, and byte-exact across the C# / TypeScript boundary.

The high-level rationale lives in [`CONCEPT.md`](CONCEPT.md); this file is the enumeration.

## Type surface (v0.1)

Supported:
- `enum` (int-backed)
- `record` (positional or property-init)
- `class` and `struct` with properties (get-only, init-only, or `get; set;`) and public fields
- Primitive types: `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `float`, `double`, `string`, `long` / `ulong` (emit as TS `bigint` with `BigInt.asIntN(64, ...)` / `asUintN(64, ...)` wrap)
- `T[]`, `List<T>`, `IReadOnlyList<T>`, `IList<T>` (all emitted as `T[]`)
- `Dictionary<K,V>`, `IReadOnlyDictionary<K,V>`, `IDictionary<K,V>` (emitted as `Record<K,V>` with string-coercible K)
- `Nullable<T>` (emitted as `T | null`)
- Transitive reachability — leaf types reached from a marked type don't need their own attribute
- Plugin remapping (`IMirrorgenExtension`) — a single user-defined plugin can map a C# domain primitive (e.g. `record OrderId(int Value)`) onto a TS primitive or a runtime-imported name. Mapped types are never emitted; their fixture values are unwrapped to the inner field.

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
  - `uint` → wrapped with `>>> 0` (every operator, including `/` and `>>`)
  - `short` / `ushort` / `byte` / `sbyte` → wrapped with explicit mask
  - Multiplication on int / uint → `Math.imul` (avoids JS Number precision loss)
  - `long` / `ulong` → `BigInt`, wrapped with `BigInt.asIntN(64, …)` / `asUintN(64, …)`;
    shift counts are masked to 6 bits to match C#
- Boolean operators and comparisons
- `if` / `else`
- `for` (C-style), `foreach` (over `T[]`, `List<T>`, `IReadOnlyList<T>`, `IList<T>`)
- `while` / `do-while` (with `break` / `continue`)
- `switch` statement (constant + enum-member patterns) and `switch` expression (constant, enum-member, relational `> < >= <= == !=`, parenthesised, `and` / `or` composites, `_`, `when` guards)
- Bitwise operators `& | ^ ~`, shifts `<< >>`, and C# 11 unsigned right shift `>>>`
- Compound assignment for all of the above (`+= -= *= /= %= &= |= ^= <<= >>= >>>=`)
- `ref` / `out` / `in` parameters (emitted as tuple destructuring at the call site)
- Generic methods (`T Identity<T>(T x)`) — without type constraints
- `throw` in the discard arm of a `switch` expression, as a totality assertion
- Method calls to other `[Transpile]` methods within the same project
- Method calls to a whitelisted set of `System.Math.*` / `System.MathF.*` functions
- `return`

Not supported in v0.1:
- LINQ in any form
- `goto`
- `yield return`
- `using`, `try` / `catch` / `finally`
- Type patterns (`int n when …`), positional / property / list patterns, recursive patterns
- Operator overloading on user types
- Generic method type constraints
- `throw` as reachable control flow (see MG0004)
- `>>>` on `long` / `ulong` — JS `BigInt` has no unsigned right shift

## Analyzer enforcement

Subset violations on `[Transpile]` members are surfaced as build errors with stable diagnostic ids:

| Id      | Severity | What it flags                                                                           |
|---------|----------|------------------------------------------------------------------------------------------|
| MG0001  | Error    | LINQ (any `System.Linq` invocation)                                                      |
| MG0002  | Error    | `async` / `await` / `Task` / `ValueTask`                                                 |
| MG0003  | Error    | `Span<T>` / any `ref struct` / `ref` returns / `unsafe` / pointer types                   |
| MG0004  | Error    | `throw`, except in the discard arm of a `switch` expression                              |
| MG0005  | Error    | `System.Reflection.*`, `System.Type` / `System.Activator` calls, `typeof(...)`           |
| MG0006  | Error    | declaring class of a `[Transpile]` method inherits from a non-`object` base              |

### Why `ref` / `out` are in but `throw` is out

Both look like "C#-isms that JavaScript lacks", and the split between them is
the same test [`CONCEPT.md`](CONCEPT.md) applies everywhere: does the construct
round-trip cleanly, and can cross-validation prove it?

`ref` / `out` / `in` **parameters** pass. They are entirely local, they emit as
tuple destructuring, and values stay values — there is no bit-level ambiguity to
mirror. (A `ref` *return* does not pass: it aliases the caller's storage, which
has no JS equivalent.)

`throw` fails both halves. The exception-type mapping is lossy — two known types
map onto `RangeError` / `TypeError` and everything else collapses to `Error`,
so C# `catch (SomeSpecificException)` and a JS `instanceof` check do not select
the same set. And a throwing path cannot be cross-validated at all: fixture
capture invokes the method and records its return value, so a throwing input
produces no fixture row.

The discard arm of a `switch` expression is the exception. A throw there asserts
the switch is total rather than describing behaviour; reaching it is already a
bug, so there is nothing for a fixture to disagree about. Mirrorgen emits a
throw in exactly that position as its own no-arm-matched safety net.

Diagnostic ids in the `MG0001`..`MG0099` range are stable. Suppressing them with `#pragma warning disable` is supported but not recommended — Mirrorgen makes no guarantee about emit correctness for suppressed cases.

## Cross-test attributes

- `[GenerateCrossTest(Samples = N, Seed = S)]` — N random samples per method, seeded for reproducibility. Required to produce any fixture rows.
- `[CrossTestCase(values...)]` — adds one explicit input row alongside the random samples. Stacks (multiple attributes are all consumed). Argument count must match the method's parameter count; values must be C# attribute constants. Useful for the corner inputs random sampling almost never hits — `int.MinValue`, `0`, `int.MaxValue`, the value that triggered an old bug.
