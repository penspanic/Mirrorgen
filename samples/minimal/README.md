# samples/minimal

The smallest end-to-end Mirrorgen pipeline: a `[Transpile]` record + enum
and three `[Transpile]` methods mirrored into TypeScript by the **MSBuild
target**, plus `[GenerateCrossTest]` on the methods proving the two sides
stay byte-equivalent.

## Layout

```
Rules/
  Rules.csproj           # imports Mirrorgen.MSBuild's target file
  Pricing.cs             # [Transpile, GenerateCrossTest] members (source of truth)
client/
  package.json           # vitest + typescript
  src/index.ts           # demo consumer
  src/_generated/
    Pricing.ts           # ← emitted by the MSBuild target on every build
    Pricing.fixtures.json # ← captured by the CLI from the just-built assembly
  test/rules.test.ts     # vitest harness that re-runs every fixture against the emit
regen.sh                 # build + fixtures + vitest in one command
```

## One-command reproduction

```bash
./regen.sh
```

What happens:

1. `dotnet build Rules.csproj` runs the Mirrorgen MSBuild target, which
   - transpiles every `[Transpile]`-marked member in `Pricing.cs` into
     `client/src/_generated/Pricing.ts`, and
   - reflects over the just-built assembly to capture C# expected outputs
     from every `[GenerateCrossTest]` method into
     `client/src/_generated/Pricing.fixtures.json`.
   Subsequent builds are no-ops as long as the sources are unchanged.
2. `vitest` re-runs every recorded sample against the emitted TS and
   asserts byte-equivalence.

A clean run prints 48 passing cross-tests (3 methods × 16 random samples each).

## How the project consumes Mirrorgen

```xml
<!-- Rules/Rules.csproj -->
<PropertyGroup>
    <MirrorgenOutput>$(MSBuildThisFileDirectory)..\client\src\_generated\</MirrorgenOutput>
    <MirrorgenSourceRoot>$(MSBuildThisFileDirectory)</MirrorgenSourceRoot>
</PropertyGroup>

<ItemGroup>
    <ProjectReference Include="…\Mirrorgen.Attributes\Mirrorgen.Attributes.csproj" />
    <ProjectReference Include="…\Mirrorgen.MSBuild\Mirrorgen.MSBuild.csproj"
                      ReferenceOutputAssembly="false" OutputItemType="" />
</ItemGroup>

<Import Project="…\Mirrorgen.MSBuild\build\Mirrorgen.MSBuild.targets" />
```

Once `Mirrorgen.MSBuild` is published, the `<Import>` collapses into a
single `<PackageReference Include="Mirrorgen.MSBuild" PrivateAssets="all" />`.

## What this sample doesn't yet show

- **Cross-validation for record-typed methods.** `[GenerateCrossTest]`
  only samples primitive + string arguments today, so the OrderLine record
  and DiscountKind enum are emitted as types but aren't fed into the
  fixture loop. The three primitive-arg methods (`ClampQuantity`, `Total`,
  `ApplyDiscount`) still get their full 16-sample cross-tests.
- **Analyzer enforcement.** The Roslyn 5.3 analyzer can't be loaded by the
  pinned .NET SDK's csc yet, so it isn't wired in here. It will be once
  the SDK catches up — at which point a subset violation in `Pricing.cs`
  becomes a build error rather than runtime nonsense.
