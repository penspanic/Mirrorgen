# samples/pricing-rules

A non-trivial Mirrorgen pipeline — bigger than `samples/minimal` so the
shape of a real consumer is visible. Covers:

- **Domain types** — a `Money` record (kept as a TS interface), `OrderLine`
  carrying a `Money` and a `ProductId`, two enums (`CustomerTier`,
  `ShippingZone`).
- **Plugin mapping** — `OrderId` / `ProductId` are collapsed onto TS
  `number` via `IMirrorgenExtension`; their declarations never reach the
  client, and cross-test fixture arguments come through as plain numbers.
- **Switch expressions** — relational patterns (`< 50`, `>= 0 and < 1000`),
  enum-member patterns, `and` composites, `_` discard arms.
- **Method composition** — `FinalPriceCents` calls `TierDiscountBps`,
  `ApplyTaxCents`, and `ShippingFeeCents` through the [Transpile] -> [Transpile]
  channel.
- **While loop** — `HalvingRounds` cross-validates an integer-division
  decay loop under random clamped input.

## Layout

```
Rules/
  Rules.csproj          # MirrorgenOutput + MirrorgenConfig + MirrorgenEmitFixtures
  Pricing.cs            # domain shapes + [Transpile, GenerateCrossTest] rules
  MirrorgenConfig.cs    # IMirrorgenExtension — maps OrderId/ProductId -> number
client/
  test/pricing.test.ts  # vitest harness over the emitted fixtures.json
  src/_generated/       # MSBuild-emitted: Pricing.ts + fixtures.json
regen.sh                # dotnet build + vitest
```

## One-command reproduction

```bash
./regen.sh
```

A clean run prints **116 passing cross-tests** (7 methods × {16 or 20}
random samples each). Every emitted TS function is byte-equivalent to the
C# original on the random inputs.

## Why everything lives in one file

The walker currently resolves `[Transpile]` reachability within a single
source file. Splitting `Pricing.cs` into a separate `Domain.cs` would
emit two TS files where the methods reference type names that aren't
declared anywhere on the TS side. The follow-up issue tracking this is
linked from the repo's open issues; until it lands, the convention is
"one source file per generated TS file."

## What it doesn't show (yet)

- `List<T>` / array arguments — `[GenerateCrossTest]` doesn't sample
  collections, so a method that takes an `OrderLine[]` would emit fine
  but skip cross-validation.
- Cross-file `[Transpile]` references (the walker:multi-file issue
  above).
- Custom generic types — explicitly out of v0.1 scope.
