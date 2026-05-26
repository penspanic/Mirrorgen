# Mirrorgen — Concept

[English](CONCEPT.md) · **한국어**

이 문서가 설계의 source of truth 입니다. README 는 공개용 얼굴이고, 여기에는 rationale, scope, roadmap 이 정리되어 있어 Mirrorgen 에 의존하기 전에 contributor 와 adopter 가 읽어야 합니다.

## 문제 정의

.NET 백엔드와 TypeScript 클라이언트를 함께 가진 비자명한 프로젝트는 결국 같은 로직을 두 벌로 구현하게 됩니다. 흔한 경우:

- **Validation rule** 을 양쪽에 두어 클라이언트가 roundtrip 없이 즉시 피드백 (이메일 형식, 비밀번호 정책, 비즈니스 룰 체크)
- **Pricing / tax / discount** 계산을 양쪽에 두어 클라이언트 프리뷰의 cart total 이 서버 source of truth 와 일치
- **권한 체크** (role/permission matrix 평가) — 서버가 거부할 액션을 UI 가 미리 숨김
- **Compact wire format 의 encoder / decoder** — packed bit, RLE, custom binary codec
- **상수** 가 독립적으로 drift — 제한값, version code, status enum 값, magic number
- **DTO 의 타입 모양** (이건 기존 도구들이 이미 해결)

타입 모양 중복은 `TypeGen`, `Tapper`, `Reinforced.Typings`, `NJsonSchema`, `SkbKontur.TypeScript.ContractGenerator`, `Typewriter`, `NSwag` 이 이미 해결합니다. 타입 모양 위의 모든 것은 오늘날 손으로 미러링되고 있고, 그 비용이 Mirrorgen 의 타겟입니다.

**가설**: 작지만 신중하게 고른 C# 부분집합만 다루는 좁고 opt-in 인 transpiler + 빌드 시점 cross-validation fixture 의 조합이, general-purpose source-to-source 컴파일러를 시도하지 않으면서도 silent drift 류 버그를 제거합니다.

## Prior art 와 Mirrorgen 의 자리

이미 잘 채워진 두 카테고리가 있고, 인접 1개 + dead predecessor 1개가 있습니다.

- **Type-only TS generator** — `TypeGen`, `Tapper`, `Reinforced.Typings`, `NSwag`, `NJsonSchema`, `Typewriter`, `TypeSync`, `SkbKontur.TypeScript.ContractGenerator`. DTO 모양을 미러링하고 method body 는 transpile 하지 않음. Mirrorgen 은 채택 프로젝트에게 이 카테고리의 superset 이 되는 게 목표 — 동일한 DTO surface + method body + cross-validation 까지. 기존 type-only consumer 가 한 번에 마이그레이션 가능.
- **Full-app C# → JS 컴파일러** — `Bridge.NET` (EOL ~2019), `H5` (활성 Bridge fork), `JSIL` (2022 archived), `Saltarelle` (2021 archived), `Script#` (legacy), 그리고 runtime variant 인 `Blazor WASM`. 클라이언트 전체를 C# 으로 작성, 큰 in-browser runtime, JS 출력 (TS 아님). "모든 C# 지원" 야망 때문에 대부분 죽었음. Mirrorgen 과 제품이 다름 — 그쪽은 클라이언트를 대체하고, 우리는 선택한 함수를 클라이언트 안으로 미러링.
- **인접: `Fable`** (F# → TS, 활성). "method transpile + TS 출력" 의 가장 가까운 건강한 선례. F# 전용, full-app 모델. C# 진영에는 동등한 게 없음.
- **가장 가까운 dead predecessor: `Rosetta`** (andry-tino, Roslyn 기반 C# → TS). README 가 "the project is dead" 라고 명시. 그 실패에서 얻은 교훈이 Mirrorgen 설계에 반영됨.

### Rosetta 가 알려주는 것

| Rosetta 의 실패 모드 | Mirrorgen 의 대응책 |
|---|---|
| 부분집합 미선언 | `docs/SUBSET.md` 에 부분집합 + 안정 diagnostic id 명시. 확장은 실제 consumer 요구가 있을 때만 |
| Codegen 시점에 에러 노출 | Roslyn analyzer (`Mirrorgen.Analyzers`) — IDE 에 빨간 줄로 표시 |
| Silent output bug | `[GenerateCrossTest]` 가 byte-exact fixture 생성, 빌드 시점 검증 |
| 도구체인 부채 (.NET FX 4.0, VS 2015) | Modern .NET, SDK-style project, NuGet 배포 |

이 네 가지 설계 결정이 합쳐서, 인접한 두 카테고리 어느 쪽으로도 무너지지 않으면서 빈 공간을 채울 수 있다는 Mirrorgen 의 베팅입니다.

## Goals

1. **공유 로직의 단일 source of truth**: C# 이 canonical, TypeScript 가 emit.
2. **예측 가능한 부분집합** — consumer 가 무엇이 컴파일되는지 정확히 알 수 있음. Transpiler 에 깜짝 놀랄 일은 있어선 안 됨.
3. **Cross-validation 을 first-class feature 로** — drift 가 production 이 아니라 빌드 시점에 검출.
4. **Domain-extensible** — 프로젝트가 자체 value type (`OrderId`, `Money`, branded primitive 등) 을 fork 없이 매핑.
5. **MSBuild target 통합으로 zero-friction** — .NET 프로젝트.
6. **기존 type-only 도구의 superset** — 한 번에 마이그레이션 가능.

## Non-goals

- General-purpose C# → TS transpiler. Non-local analysis 가 필요하거나, JavaScript 를 거쳐 round-trip 시 의미가 깨지는 기능은 거절.
- Runtime interop. Mirrorgen 은 정적 `.ts` 파일 생성만 함. 런타임에 .NET 으로 콜백하지 않음.
- TS → C# 방향. 단방향.
- v1 에 source map. 주석 기반 traceability (`// from src/Foo.cs:42`) 가 충분하다고 입증되기 전까지.

## 아키텍처

```
                ┌────────────────────────────────────────┐
   user code -> │ [Transpile] attribute on type / method │
                └────────────────────────────────────────┘
                              │
                              ▼
                ┌────────────────────────────────────────┐
                │ Mirrorgen.Analyzers (IDE)              │ ← subset 위반 여기서 잡힘
                └────────────────────────────────────────┘
                              │
                              ▼ (build)
                ┌────────────────────────────────────────┐
                │ Mirrorgen.MSBuild  →  Mirrorgen.Core   │
                │   - Roslyn SyntaxTree + Semantic model │
                │   - Type reachability walk             │
                │   - Subset 검증 (defensive)            │
                │   - TS AST 구성                        │
                │   - .ts 파일 emit                      │
                │   - Cross-test fixture (C# 측) emit    │
                └────────────────────────────────────────┘
                              │
            ┌─────────────────┼─────────────────┐
            ▼                                   ▼
  _generated/types.ts                _generated/fixtures/*.json
  _generated/rules.ts                                   │
            │                                           ▼
            ▼                              TS test suite 의 vitest 가 소비
  손으로 작성한 클라이언트 코드가 소비
```

Core 라이브러리만이 실제 일을 합니다. 나머지 프로젝트는 얇은 wrapper — CLI, MSBuild, analyzer surface, attribute 정의. 이 구조 덕분에 Core 는 unit-testable 하고, 엔진을 건드리지 않고 새 진입점 (예: IDE plugin 용 programmatic API) 을 추가할 수 있습니다.

## 부분집합

부분집합을 **type surface** 와 **expression surface** 로 나눕니다. Type surface 는 더 쉬운 절반이고, Mirrorgen 이 기존 type-only generator 의 drop-in superset 으로 동작하는 근거.

### Type surface (v0.1)

지원:
- `enum` (int-backed)
- `record` (positional 또는 property-init)
- `class` 와 `struct` (get-only / init-only property 만)
- Primitive: `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long` (BigInt), `float`, `double`, `string`
- `T[]`, `List<T>`, `IReadOnlyList<T>` (모두 `T[]` 로 emit)
- `Dictionary<K,V>`, `IReadOnlyDictionary<K,V>` (`Record<K,V>` 로 emit, K 는 string-coercible)
- `Nullable<T>` (`T | null` 로 emit)
- Transitive reachability — marked type 에서 도달 가능한 leaf type 은 자체 attribute 불필요

v0.1 에 지원 안 함:
- 생성 이후 mutable collection
- `Tuple<...>` / `ValueTuple` — record 권장
- 사용자 정의 generic type (v0.3 으로 연기)
- Inheritance / interface implementation surface

### Expression / statement surface (v0.1)

지원:
- Local variable (`var`, `int`, `bool`, ...)
- Integer type 산술 — 올바른 wrap semantic 보존:
  - `int` → `| 0` 으로 wrap
  - `uint` → `>>> 0` 으로 wrap
  - `short` / `ushort` / `byte` / `sbyte` → 명시적 mask 로 wrap
  - int / uint 곱셈 → `Math.imul` (JS Number 정밀도 손실 회피)
- Boolean operator, 비교
- `if` / `else`
- `for` (C-style), `foreach` (v0.1 에선 `T[]` 만)
- `switch` statement, `switch` expression (enum 의 constant pattern + type pattern)
- 같은 프로젝트의 다른 `[Transpile]` 메서드 호출
- 허용 리스트의 `System.Math.*` / `System.MathF.*` 함수 호출
- `return`

v0.1 에 지원 안 함:
- `while`, `do-while` (generated code 에 unbounded loop 를 정말 원하는지 확신 후 v0.2)
- 모든 형태의 LINQ
- `goto`
- `yield return`
- `using`, `try` / `catch` / `finally`
- enum constant 이상의 pattern matching
- 사용자 타입에 대한 operator overloading
- Generic method

지원 안 되는 기능이 `[Transpile]` 멤버에 적용되면 analyzer 가 안정 diagnostic id (`MG0001` … `MG0099`) 로 보고 — 사용자가 suppress 하거나 지원 대기 가능.

## Attribute surface

Attribute 패키지는 의도적으로 작음 — attribute 3개, runtime 의존성 0 — consumer 가 Roslyn 이나 analyzer 코드를 끌어들이지 않고 참조 가능.

```csharp
[Transpile]                         // type, method, 또는 property 에 적용
[Transpile(emitName: "isInRange")]  // TS identifier 오버라이드

[GenerateCrossTest(samples: 1000, seed: 42)]  // method 전용; fixture emit

[assembly: TranspileAssembly]      // 단축: 어셈블리의 모든 public 멤버
```

`[GenerateCrossTest]` 는 모든 입출력 타입이 `DatraJson` 호환 룰로 JSON 직렬화 가능해야 함 (이미 type surface 와 공유하는 제약).

## Cross-validation

Mirrorgen 을 경쟁자와 구분하는 feature.

`[GenerateCrossTest]` 가 붙은 모든 메서드에 대해 Mirrorgen 이 생성:

1. Transpile 된 메서드를 담은 TypeScript 파일 (`_generated/rules.ts`)
2. 실행 시 N 개의 랜덤 입력과 *원본 C# 메서드* 로 계산한 기대 출력을 dump 하는 C# fixture-generation 테스트 (`_generated/MirrorgenFixtures.cs`)
3. fixture 를 로드해서 TypeScript 구현이 동일한 출력을 내는지 assert 하는 vitest spec (`_generated/cross.test.ts`)

워크플로:

```bash
dotnet test           # fixture generator 도 같이 실행, JSON 생성
npm test              # vitest 가 JSON 읽고 TS 출력과 비교
```

두 구현이 한 비트라도 어긋나면 TS test 가 expected vs actual diff 와 함께 실패.

이 말은 transpiler 가 *증명 가능하게* 옳을 필요는 없다는 뜻 — 충분히 큰 입력 샘플에서 *관찰 가능하게* 옳기만 하면 됨. Transpiler 의 버그가 silent client/server desync 가 아니라 빨간 CI 로 드러납니다.

## 도메인 타입 매핑 plugin

실제 프로젝트에는 custom 매핑이 필요한 value type 이 있습니다:
- `OrderId : struct { int Value }` 는 TS 에서 `{ value: number }` 가 아니라 `number` 가 되어야 함
- `Money` 는 산술 메서드가 있는 runtime helper class (또는 branded type) 로 export
- 상태 enum 은 numeric enum 대신 TS string literal union 으로 매핑할 수도 있음

Plugin API:

```csharp
public sealed class MyConfig : IMirrorgenExtension
{
    public void Configure(IMirrorgenBuilder b)
    {
        // primitive number 로 취급
        b.MapType<OrderId>(ts => ts.AsPrimitive("number"));

        // runtime-provided class 로 취급
        b.MapType<Money>(ts => ts.RuntimeImport("Money"));

        // enum 을 string literal union 으로 취급
        b.MapEnumAsStringUnion<OrderStatus>();

        // 전역 identifier 표기 컨벤션 오버라이드
        b.NamingConvention = NamingConvention.CamelCase;
    }
}
```

MSBuild 가 `<MirrorgenConfig>` property 로 config 타입을 발견 — DI 컨테이너 불필요.

## Type-only generator 로부터의 마이그레이션

Mirrorgen v0.1 은 typical type-only generator 모양의 drop-in superset 으로 설계됩니다: record / enum / class 에 붙는 `[Export]` 류 attribute, Roslyn 기반 reachability scan, TS interface + enum emit.

기존 채택자의 마이그레이션 경로:

1. 기존 export attribute 를 `[Transpile]` 로 교체 — `using` alias (예: `using LegacyExport = MirrorgenTranspile;`) 또는 일회성 codemod.
2. 이전 generator 의 `.ts` 산출물을 golden file 로 취급. 같은 입력에 Mirrorgen 을 돌려 wire-equivalent (또는 byte-equivalent) 결과가 나오는지 검증 후 전환.
3. 이전 generator 패키지를 제거하고 `<PackageReference Include="Mirrorgen.MSBuild" />` 추가.

실제 채택자의 기존 출력에 대한 wire-equivalence 가 v0.1 의 "done" 기준. 프로젝트의 클라이언트 suite 가 Mirrorgen-emit 타입에 대해 여전히 pass 하면 v0.1 ship.

## Roadmap

### v0.1 — 기존 type-only generator 와 type parity + 최소 method transpile
- 위 spec 의 type surface
- Attribute 와 analyzer 배치
- Sample project 에서 MSBuild target 동작
- Method 부분집합: primitive 위의 pure 함수, 아직 array/foreach 없음
- Pure method 에 대한 cross-validation fixture 파이프라인
- 기존 type-only generator 의 출력에 대해 wire-equivalent 마이그레이션 검증

### v0.2 — 실제 method 워크로드
- Array 와 `foreach`
- enum 위의 `switch` expression
- `[Transpile]` 메서드 간 호출
- 첫 실세계 채택자: 비자명한 validation / pricing / permission 로직의 production-grade method transpile

### v0.3 — 사용자 정의 generic + runtime helper
- 사용자 generic type (`Pool<T>` 같은 구조)
- `@mirrorgen/runtime` 의 옵션형 fixed-point 산술 helper (opt-in, core 아님)
- `Mirrorgen.Analyzers` 다듬기 (diagnostic 개선, code fix)

### v0.4 — Packed buffer 와 typed-array-backed struct
- Transpile 된 struct 의 `ushort[]` / `byte[]` 필드를 TS 의 typed array 로 매핑
- Typed-array backing 을 가진 idiomatic TS class 생성
- 가장 어려운 milestone — typed array 위 byte-exact 산술은 용서가 없음. 추측이 아니라 실제 수요에 의해서만 진행.

### v1.0 — 안정화
- 부분집합 freeze
- Diagnostic id 안정화
- API surface 안정화
- Multi-target 지원 (runtime npm 패키지가 browser + node + worker 지원)

## Open question

- **`long` → `BigInt` 인가 `number` 인가?** `BigInt` 기본은 안전하지만 ergonomic 비용 큼. Plugin 으로 타입별 오버라이드가 유력 답.
- **`decimal`?** 실제 use case 가 요청하기 전까지 scope 밖.
- **Source map?** v0.x 는 주석만으로 traceability. 실 사용자가 불평하면 재검토.
- **Multi-language target?** 지금은 아님. Python/Kotlin 추가는 surface 두 배. 우리가 풀 문제 아님.

## Naming 과 License

- 이름: **Mirrorgen** — "mirror" 은유가 핵심 설계 아이디어 (C# 과 TS 가 lockstep 으로 유지되는 reflection). "TsGen / TypeGen / TypeScriptGenerator" 가 빽빽한 네임스페이스에서 충돌 없이 깨끗하게 읽힘.
- License: MIT.
- Repository: `penspanic/Mirrorgen` (README 와 이 문서 최종 확정 후 생성).
