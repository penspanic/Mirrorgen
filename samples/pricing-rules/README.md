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
- **Array argument** — `CartSubtotalCents(OrderLine[])` sums an array of
  records sampled at random length 0..8. Exercises array fixture
  sampling end to end.

## Layout

```
Rules/
  Rules.csproj          # MirrorgenOutput + MirrorgenConfig + MirrorgenEmitFixtures
  Domain.cs             # [Transpile] records / enums / wrapper structs
  Pricing.cs            # [Transpile, GenerateCrossTest] methods
  MirrorgenConfig.cs    # IMirrorgenExtension — maps OrderId/ProductId -> number
client/
  test/pricing.test.ts  # vitest harness over the emitted fixtures.json
  src/_generated/       # MSBuild-emitted: Pricing.ts + fixtures.json
regen.sh                # dotnet build + vitest
```

The walker resolves reachability across the whole project at once, so
methods in `Pricing.cs` that reference a record or enum in `Domain.cs`
inline that declaration into the same `Pricing.ts` they emit into.
Files that only declare types (no `[Transpile]` methods of their own)
don't produce their own `.ts` file — they're picked up by whichever
rules file consumes them.

## One-command reproduction

```bash
./regen.sh
```

A clean run prints **116 passing cross-tests** (7 methods × {16 or 20}
random samples each). Every emitted TS function is byte-equivalent to the
C# original on the random inputs.

## What it doesn't show (yet)

- Custom generic types — explicitly out of v0.1 scope.
