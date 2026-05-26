# Mirrorgen — Concept

**English** · [한국어](CONCEPT_ko.md)

This document is the design source of truth. README is the public face; this is the rationale, scope, and roadmap that contributors and adopters should read before relying on Mirrorgen.

## Problem statement

Any non-trivial project that pairs a .NET backend with a TypeScript client ends up maintaining two implementations of the same logic. The common cases:

- **Validation rules** repeated in both places so the client can give immediate feedback without a roundtrip (email shape, password policy, business-rule checks)
- **Pricing / tax / discount math** so the cart total matches between client preview and server source of truth
- **Permission checks** (role/permission matrix evaluation) so the UI hides actions the server would refuse
- **Encoders / decoders** for compact wire formats — packed bits, RLE, custom binary codecs
- **Constants** that drift independently — limits, version codes, status enum values, magic numbers
- **Type shape** of DTOs (this part is already solved by existing tools)

The type-shape duplication is already addressed by `TypeGen`, `Tapper`, `Reinforced.Typings`, `NJsonSchema`, `SkbKontur.TypeScript.ContractGenerator`, `Typewriter`, and `NSwag`. Everything above the type-shape line is hand-mirrored today, and that's the cost Mirrorgen targets.

**Hypothesis**: a narrow, opt-in transpiler that handles a *small but carefully chosen* subset of C# — combined with build-time cross-validation fixtures — eliminates the silent-drift class of bugs without trying to be a general-purpose source-to-source compiler.

## Prior art and where Mirrorgen fits

Two well-populated categories exist, plus one adjacent and one dead predecessor.

- **Type-only TS generators** — `TypeGen`, `Tapper`, `Reinforced.Typings`, `NSwag`, `NJsonSchema`, `Typewriter`, `TypeSync`, `SkbKontur.TypeScript.ContractGenerator`. Mirror DTO shape; none transpile method bodies. Mirrorgen aims to be a superset for the projects that adopt it — same DTO surface plus method bodies plus cross-validation, so an existing type-only consumer can migrate in one step.
- **Full-app C# → JS compilers** — `Bridge.NET` (EOL ~2019), `H5` (active Bridge fork), `JSIL` (archived 2022), `Saltarelle` (archived 2021), `Script#` (legacy), and `Blazor WASM` for the runtime variant. Write your whole client in C#, ship a substantial in-browser runtime, target JS not TS. The "support all of C#" ambition is why most of them are dead. Different product than Mirrorgen — they replace your client, we mirror selected functions into it.
- **Adjacent: `Fable`** (F# → TS, active). The closest healthy precedent for "method transpile with TS output". F# only, full-app model. C# equivalent doesn't exist.
- **Closest dead predecessor: `Rosetta`** (andry-tino, C# → TS via Roslyn). README states "the project is dead". Lessons from its failure shape Mirrorgen's design.

### What Rosetta teaches

| Rosetta's failure mode | Mirrorgen's countermeasure |
|---|---|
| Unbounded subset | Subset declared in `docs/SUBSET.md` with stable diagnostic ids; expansion only on real-consumer demand |
| Errors surfaced at codegen time | Roslyn analyzer (`Mirrorgen.Analyzers`) — violations are red squiggles in the IDE |
| Silent output bugs | `[GenerateCrossTest]` produces byte-exact fixtures verified at build time |
| Toolchain debt (.NET FX 4.0, VS 2015) | Modern .NET, SDK-style projects, NuGet-distributed |

These four design choices, together, are Mirrorgen's wager that the gap is fillable without collapsing into either neighboring category.

## Goals

1. **Single source of truth** for shared logic: C# is canonical, TypeScript is emitted.
2. **Predictable subset** so consumers know exactly what compiles. Surprise belongs nowhere in a transpiler.
3. **Cross-validation as a first-class feature** — drift is detected at build time, not in production.
4. **Domain-extensible** — projects map their own value types (e.g. `OrderId`, `Money`, branded primitives) without forking.
5. **Zero-friction integration** for .NET projects via MSBuild target.
6. **Superset of existing type-only tools** so projects can migrate off them in one step.

## Non-goals

- General-purpose C# → TS transpiler. We will reject features that require non-local analysis or whose semantics don't round-trip cleanly through JavaScript.
- Runtime interop. Mirrorgen produces static `.ts` files; it does not call back into .NET at runtime.
- TS → C# direction. One-way only.
- Source maps in v1. Comment-based traceability (`// from src/Foo.cs:42`) is enough until proven otherwise.

## Architecture

```
                ┌────────────────────────────────────────┐
   user code -> │ [Transpile] attribute on type / method │
                └────────────────────────────────────────┘
                              │
                              ▼
                ┌────────────────────────────────────────┐
                │ Mirrorgen.Analyzers (IDE)              │ ← subset violations caught here
                └────────────────────────────────────────┘
                              │
                              ▼ (build)
                ┌────────────────────────────────────────┐
                │ Mirrorgen.MSBuild  →  Mirrorgen.Core   │
                │   - Roslyn SyntaxTree + Semantic model │
                │   - Type reachability walk             │
                │   - Subset verification (defensive)    │
                │   - TS AST construction                │
                │   - Emit .ts files                     │
                │   - Emit cross-test fixtures (C# side) │
                └────────────────────────────────────────┘
                              │
            ┌─────────────────┼─────────────────┐
            ▼                                   ▼
  _generated/types.ts                _generated/fixtures/*.json
  _generated/rules.ts                                   │
            │                                           ▼
            ▼                              consumed by vitest in TS test suite
  consumed by hand-written client code
```

The Core library is the only piece that does real work. The other projects are thin wrappers — CLI, MSBuild, analyzer surface, attribute definitions. This keeps Core unit-testable and lets us add new entry points (e.g. a programmatic API for IDE plugins) without touching the engine.

## The subset

The precise list of supported types, expressions, and the analyzer-id mapping that enforces it lives in **[`SUBSET.md`](SUBSET.md)**. This section is the rationale.

The subset is split in two: **type surface** (DTO shapes — enums, records, primitives, `T[]`, `Dictionary<K,V>`, etc.) and **expression / statement surface** (locals, integer arithmetic with wrap, control flow, calls between `[Transpile]` methods).

Out of scope in v0.1: LINQ, async / await / Task, `Span<T>` / ref / unsafe / pointers, `throw`, reflection, inheritance, mutable collection mutation, generic methods, pattern matching beyond enum constants.

Why narrow? The output has to mirror C# semantics bit-for-bit, and every additional construct multiplies the cross-language edge cases (overflow, deferred enumeration, allocation behavior, exception semantics). Mirrorgen leans on `Mirrorgen.Analyzers` to fail subset violations at build time (stable ids `MG0001`–`MG0099`) rather than producing TS that quietly drifts from the C# source.

## Attribute surface

The attribute package is intentionally tiny — three attributes, zero runtime dependencies — so consumers can reference it without dragging in Roslyn or any analyzer code.

```csharp
[Transpile]                         // applies to type, method, or property
[Transpile(emitName: "isInRange")]  // override the TS identifier

[GenerateCrossTest(samples: 1000, seed: 42)]  // method-only; emits fixture

[assembly: TranspileAssembly]      // shortcut: every public member in the assembly
```

`[GenerateCrossTest]` requires that all input and return types are JSON-serializable with `DatraJson`-compatible rules (a constraint already shared with the type surface).

## Cross-validation

This is the feature that separates Mirrorgen from competitors.

For every method marked `[GenerateCrossTest]`, Mirrorgen generates:

1. A TypeScript file (`_generated/rules.ts`) containing the transpiled method
2. A C# fixture-generation test (`_generated/MirrorgenFixtures.cs`) that, when run, produces N random inputs and computes expected outputs using the *original C# method*
3. A vitest spec (`_generated/cross.test.ts`) that loads the fixture and asserts the TypeScript implementation produces identical outputs

Workflow:

```bash
dotnet test           # also runs the fixture generator, producing JSON
npm test              # vitest reads JSON, compares against TS output
```

If the two diverge by a single bit on any sample, the TS test fails with a diff between expected and actual.

This means the transpiler doesn't have to be provably correct — it has to be observably correct on a large enough sample of inputs. Bugs in the transpiler manifest as red CI, not as silent client/server desync.

## Domain-type mapping plugin

Real projects have value types that need custom mapping:
- `OrderId : struct { int Value }` should become `number` in TS, not `{ value: number }`
- `Money` should be exported as a runtime helper class with arithmetic methods (or a branded type)
- A status enum might map to a TS string literal union instead of a numeric enum

The plugin API:

```csharp
public sealed class MyConfig : IMirrorgenExtension
{
    public void Configure(IMirrorgenBuilder b)
    {
        // Treat as primitive number
        b.MapType<OrderId>(ts => ts.AsPrimitive("number"));

        // Treat as runtime-provided class
        b.MapType<Money>(ts => ts.RuntimeImport("Money"));

        // Treat as string literal union from an enum
        b.MapEnumAsStringUnion<OrderStatus>();

        // Override identifier casing globally
        b.NamingConvention = NamingConvention.CamelCase;
    }
}
```

MSBuild discovers the config type via `<MirrorgenConfig>` property — no DI container needed.

## Migration from type-only generators

Mirrorgen v0.1 is designed as a drop-in superset of the typical type-only generator shape: an `[Export]`-style attribute on records / enums / classes, Roslyn-based reachability scan, emission of TS interfaces + enums.

Migration path for an existing adopter:

1. Replace the existing export attribute with `[Transpile]` — via a `using` alias (e.g. `using LegacyExport = MirrorgenTranspile;`) or a one-off codemod.
2. Treat the previous generator's `.ts` artifact as a golden file. Run Mirrorgen against the same input and require a wire-equivalent (or byte-equivalent) result before cutting over.
3. Drop the previous generator package and add `<PackageReference Include="Mirrorgen.MSBuild" />`.

Wire-equivalence on a real adopter's existing output is the v0.1 "done" criterion. If a project's client suite still passes against Mirrorgen-emitted types, v0.1 ships.

## Roadmap

### v0.1 — Type parity with existing type-only generators + minimal method transpile
- Type surface as specified above
- Attributes and analyzers in place
- MSBuild target works on a sample project
- Method subset: pure functions over primitives, no array/foreach yet
- Cross-validation fixture pipeline for pure methods
- Wire-equivalent migration verified against an existing type-only generator's output

### v0.2 — Real method workloads
- Arrays and `foreach`
- `switch` expression on enums
- Calls between `[Transpile]` methods
- First real-world adopter: production-grade method transpile for non-trivial validation / pricing / permission logic

### v0.3 — User-defined generic types and runtime helpers
- User generic types (`Pool<T>`-style structures)
- Optional fixed-point arithmetic helpers in `@mirrorgen/runtime` (opt-in, not core)
- `Mirrorgen.Analyzers` polish (better diagnostics, code fixes)

### v0.4 — Packed buffers and typed-array-backed structs
- Map `ushort[]` / `byte[]` fields on a transpiled struct to typed arrays in TS
- Generate idiomatic TS classes that own the typed-array backing
- Hardest milestone — byte-exact arithmetic over typed arrays is unforgiving. Driven by real demand, not speculation.

### v1.0 — Stability
- Subset frozen
- Diagnostic ids stable
- API surface stable
- Multi-target support (the runtime npm package supports browser + node + workers)

## Open questions

- **`long` → `BigInt` or `number`?** Defaulting to `BigInt` is safe but ergonomically painful. Per-type override via plugin is the likely answer.
- **Decimal / `decimal`?** Out of scope until a real use case asks for it.
- **Source maps?** Comment-only traceability for v0.x; revisit if real users complain.
- **Multi-language target?** Not now. Adding Python/Kotlin doubles the surface and we're not solving that.

## Naming and license

- Name: **Mirrorgen** — the "mirror" metaphor is the central design idea (C# and TS as reflections that the tool keeps in lockstep), and the name reads cleanly without colliding in the heavily-occupied "TsGen / TypeGen / TypeScriptGenerator" namespace.
- License: MIT.
- Repository: `penspanic/Mirrorgen` (to be created once README and this document are finalized).
