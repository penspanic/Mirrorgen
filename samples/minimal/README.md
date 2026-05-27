# samples/minimal

The smallest end-to-end Mirrorgen pipeline: a `[Transpile]` record + enum
and three `[Transpile]` methods mirrored into TypeScript, with
`[GenerateCrossTest]` on the methods proving the two sides stay
byte-equivalent.

## Layout

```
Rules/
  Rules.csproj           # references Mirrorgen.Attributes
  Pricing.cs             # [Transpile, GenerateCrossTest] methods (the source of truth)
client/
  package.json           # vitest + typescript
  src/index.ts           # demo consumer
  src/_generated/
    rules.ts             # ← emitted by `mirrorgen transpile`
    rules.fixtures.json  # ← emitted by `mirrorgen fixtures` (C# expected outputs)
  test/rules.test.ts     # vitest harness that re-runs every fixture against rules.ts
regen.sh                 # one command for the whole loop
```

## One-command reproduction

```bash
./regen.sh
```

The script builds the CLI + the Rules assembly, transpiles
`Rules/Pricing.cs` to `client/src/_generated/rules.ts`, captures C# expected
outputs into `rules.fixtures.json`, then runs `vitest`. A clean run prints
48 passing cross-tests (3 methods × 16 random samples each).

## What this sample doesn't yet show

- **Cross-validation for record-typed methods.** `[GenerateCrossTest]` only
  samples primitive + string arguments today, so the OrderLine record and
  DiscountKind enum are emitted as types but aren't fed into the fixture
  loop. The three primitive-arg methods (`ClampQuantity`, `Total`,
  `ApplyDiscount`) still get their full 16-sample cross-tests.
- **MSBuild integration.** Today regeneration is driven by `regen.sh` and
  the CLI. Once the `.targets` package ships, a `dotnet build` on the
  `Rules.csproj` will do the same work automatically and `regen.sh` will
  collapse into "just build the project."
- **Analyzer enforcement.** The Roslyn 5.3 analyzer can't be loaded by the
  pinned .NET SDK's csc yet, so it isn't wired in here. It will be once the
  SDK catches up — at which point a subset violation in `Pricing.cs` becomes
  a build error rather than runtime nonsense.
