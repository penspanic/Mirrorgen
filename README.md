# Mirrorgen

**English** · [한국어](README_ko.md)

**Transpile C# types *and* pure logic into TypeScript — with cross-validation that proves the two stay in lockstep.**

Existing C#-to-TypeScript generators stop at type shape: enums, records, DTO interfaces. That leaves *logic mirrors* — validation rules, pricing and tax math, permission checks, compact wire-format codecs — duplicated by hand on both sides of the JS/.NET boundary. Mirrorgen targets the half that's actually expensive to maintain.

```csharp
// C# (source of truth)
[Transpile]
public static bool IsWithinDistance(int x1, int y1, int x2, int y2, int radius)
{
    var dx = x2 - x1;
    var dy = y2 - y1;
    return dx * dx + dy * dy <= radius * radius;
}
```

```ts
// _generated/rules.ts — emitted at build time
export function isWithinDistance(x1: number, y1: number, x2: number, y2: number, radius: number): boolean {
    const dx = (x2 - x1) | 0;
    const dy = (y2 - y1) | 0;
    return Math.imul(dx, dx) + Math.imul(dy, dy) <= Math.imul(radius, radius);
}
```

And the part nobody else does — Mirrorgen emits a **fixture cross-test**: C# generates random inputs and expected outputs at build time, the TypeScript test consumes the same fixture, and CI fails the moment the two implementations diverge by a single bit.

## Why

Any project with a .NET backend (ASP.NET, SignalR, Blazor Server, a custom simulation) plus a TypeScript client ends up maintaining two copies of the same logic — and the cost is **silent drift**, not duplication itself. A constant changes on one side, the other forgets, a bug ships. Existing tools can't help because they only mirror data shape.

Mirrorgen exists to make logic mirrors as cheap as type mirrors:

- One source of truth: C#
- One opt-in marker: `[Transpile]`
- Generated TypeScript ships next to the consuming code
- Cross-validation fixtures keep the two byte-exact, forever

## Where Mirrorgen fits

Existing tools cluster into two groups: **type-only generators** that mirror DTO shape but not logic (`TypeGen`, `Tapper`, `Reinforced.Typings`, `NSwag`), and **full-app C# → JS compilers** that let you write the whole client in C# at the cost of a substantial in-browser runtime (`Bridge.NET`, `H5`, `JSIL`, `Blazor WASM`). F# teams have `Fable` for the equivalent F# → TS story. Mirrorgen targets the gap: plain `.ts` output that mirrors selected C# logic alongside a hand-written TypeScript client.

|                       | Mirrorgen | Type-only generators | Full-app C# → JS | Fable (F# → TS) |
|-----------------------|-----------|----------------------|------------------|-----------------|
| C# logic body         | narrow subset | ❌               | full C#          | n/a (F#)        |
| Output                | **TypeScript** | TypeScript     | JavaScript        | TypeScript or JS |
| Client runtime cost   | KB (tiny helper) | none          | hundreds of KB+   | varies          |
| Cross-validation fixtures | ✅    | ❌                  | ❌                | ❌              |
| Subset-enforcing analyzer | ✅    | ❌                  | n/a              | n/a             |
| Use model             | mirror logic next to hand-written TS | mirror DTOs | write whole client in C# | write whole client in F# |

If you want to write your whole client in C# or F#, use `H5` / `Blazor WASM` / `Fable`. If you only need DTO shapes mirrored, `TypeGen` or `Tapper` is smaller and battle-tested. Mirrorgen exists because hand-mirroring rules / pricing / validation / codecs while keeping them in lockstep with C# is what burns time, and none of those columns targets it. The closest C# → TS method transpiler attempt (`Rosetta` by andry-tino) is explicitly dead in its README; lessons from why it stalled inform Mirrorgen's design and are written up in [`docs/CONCEPT.md`](docs/CONCEPT.md).

## What it doesn't do (on purpose)

Mirrorgen is a *subset* transpiler. It does **not** try to translate arbitrary C#. The supported subset is intentionally narrow so the output stays predictable, debuggable, and byte-exact:

- ❌ `async` / `await`, `Task`, threading
- ❌ LINQ, deferred enumerables
- ❌ `Span<T>`, `ref`, `unsafe`, pointers
- ❌ Exceptions (return result types instead)
- ❌ Reflection
- ❌ Inheritance and virtual dispatch

A Roslyn analyzer marks any `[Transpile]` member that crosses these lines as a build error — you find out in your IDE, not at codegen time.

See [`docs/CONCEPT.md`](docs/CONCEPT.md) for the precise subset spec and roadmap.

## Status

**Pre-alpha.** Design is being finalized. No published packages yet.

Watch / star if you want to be pinged when v0.1.0 lands.

## How it will fit into a project

```xml
<!-- YourProject.Rules.csproj -->
<ItemGroup>
    <PackageReference Include="Mirrorgen.Attributes" Version="0.1.0" />
    <PackageReference Include="Mirrorgen.Analyzers" Version="0.1.0" PrivateAssets="all" />
    <PackageReference Include="Mirrorgen.MSBuild" Version="0.1.0" PrivateAssets="all" />
</ItemGroup>

<PropertyGroup>
    <MirrorgenOutput>$(MSBuildThisFileDirectory)../YourProject.Client/src/_generated/</MirrorgenOutput>
    <MirrorgenConfig>YourProject.MirrorgenConfig</MirrorgenConfig>
</PropertyGroup>
```

```csharp
// Domain type mapping plugin
public sealed class MirrorgenConfig : IMirrorgenExtension
{
    public void Configure(IMirrorgenBuilder b)
    {
        b.MapType<OrderId>(ts => ts.AsPrimitive("number"));
        b.MapType<Money>(ts => ts.RuntimeImport("Money"));
    }
}
```

The TypeScript side gets a tiny runtime helper package (`@mirrorgen/runtime`) for integer arithmetic, equality, and pluggable domain helpers — kept under a few KB so the generated code stays clean.

## License

MIT.

## Repository layout

```
mirrorgen/
  src/
    Mirrorgen.Core/         # Roslyn-based transpiler engine
    Mirrorgen.Attributes/   # [Transpile], [GenerateCrossTest] — zero-dep, ref'd by users
    Mirrorgen.Analyzers/    # Roslyn analyzer enforcing the subset
    Mirrorgen.MSBuild/      # MSBuild target wrapper
    Mirrorgen.Cli/          # `dotnet mirrorgen` tool
  runtime-ts/               # @mirrorgen/runtime npm package
  samples/
    minimal/                # smallest possible C# -> TS example
    pricing-rules/          # non-trivial sample with domain-type mapping
  tests/
    cross-validation/       # Mirrorgen self-validates against its own samples
  docs/
    CONCEPT.md              # this is where the design lives
    SUBSET.md               # exact subset spec
    ROADMAP.md
```
