# Mirrorgen

[![NuGet](https://img.shields.io/nuget/v/Mirrorgen.Attributes.svg)](https://www.nuget.org/packages/Mirrorgen.Attributes)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[English](README.md) · **한국어**

**C# 의 타입과 pure 로직을 TypeScript 로 transpile — 두 구현이 lockstep 으로 일치함을 cross-validation 으로 증명합니다.**

기존 C#→TypeScript generator 는 타입 모양(enum, record, DTO interface)까지만 다룹니다. 그 결과 *로직 미러* — validation rule, 가격/세금 계산, 권한 체크, compact wire-format codec — 가 JS/.NET 경계 양쪽에서 손으로 중복 관리됩니다. Mirrorgen 은 유지보수 비용이 실제로 큰 그 절반을 노립니다.

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
// _generated/rules.ts — 빌드 시점에 자동 생성
export function isWithinDistance(x1: number, y1: number, x2: number, y2: number, radius: number): boolean {
    const dx = (x2 - x1) | 0;
    const dy = (y2 - y1) | 0;
    return Math.imul(dx, dx) + Math.imul(dy, dy) <= Math.imul(radius, radius);
}
```

그리고 다른 어떤 도구도 하지 않는 한 가지 — Mirrorgen 은 **fixture cross-test** 를 생성합니다. C# 이 빌드 시점에 랜덤 입력과 기대 출력을 dump 하고, TypeScript test 가 같은 fixture 를 소비합니다. 두 구현이 한 비트라도 어긋나면 CI 가 즉시 실패합니다.

## 왜 필요한가

.NET 백엔드(ASP.NET, SignalR, Blazor Server, 또는 자체 simulation)와 TypeScript 클라이언트를 함께 가진 프로젝트는 결국 같은 로직을 양쪽에 두 벌로 유지하게 됩니다. 진짜 비용은 중복 자체가 아니라 **조용한 drift** 입니다. 한쪽 상수가 바뀌었는데 반대쪽이 잊고, 그대로 버그가 출시됩니다. 기존 도구는 데이터 모양만 미러링하므로 이 문제를 해결하지 못합니다.

Mirrorgen 은 로직 미러를 타입 미러만큼 싸게 만드는 게 목표입니다:

- 단일 source of truth: C#
- 단일 opt-in marker: `[Transpile]`
- 생성된 TypeScript 가 소비처 옆에 함께 배치
- Cross-validation fixture 가 두 구현을 영구적으로 byte-exact 유지

## Mirrorgen 의 자리

기존 도구는 두 그룹으로 나뉩니다. **Type-only generator** (`TypeGen`, `Tapper`, `Reinforced.Typings`, `NSwag`) 는 DTO 모양만 미러링하고 로직은 다루지 않습니다. **Full-app C#→JS 컴파일러** (`Bridge.NET`, `H5`, `JSIL`, `Blazor WASM`) 는 클라이언트 전체를 C# 으로 작성하게 해주지만 큰 in-browser runtime 비용을 동반합니다. F# 진영에는 `Fable` 이 같은 역할을 합니다. Mirrorgen 은 그 사이의 빈 공간을 노립니다 — 손으로 작성한 TypeScript 클라이언트 옆에 선택한 C# 로직을 깨끗한 `.ts` 로 미러링.

|                       | Mirrorgen | Type-only generator | Full-app C# → JS | Fable (F# → TS) |
|-----------------------|-----------|----------------------|------------------|-----------------|
| C# 로직 본문          | 좁은 부분집합 | ❌               | full C#          | n/a (F#)        |
| 출력                  | **TypeScript** | TypeScript     | JavaScript        | TypeScript 또는 JS |
| 클라이언트 런타임 비용 | KB (작은 helper) | 없음          | 수백 KB 이상      | 다양함          |
| Cross-validation fixture | ✅    | ❌                  | ❌                | ❌              |
| Subset 강제 analyzer  | ✅        | ❌                  | n/a              | n/a             |
| 사용 모델             | 손으로 짠 TS 옆에 로직 미러 | DTO 미러 | 전체 클라이언트를 C# 으로 | 전체 클라이언트를 F# 으로 |

전체 클라이언트를 C# 또는 F# 으로 짜고 싶다면 `H5` / `Blazor WASM` / `Fable` 을 쓰세요. DTO 모양만 미러링하면 충분하다면 `TypeGen` 이나 `Tapper` 가 더 작고 검증된 도구입니다. Mirrorgen 이 존재하는 이유는, rule / 가격 / validation / codec 을 손으로 미러링하면서 C# 과 lockstep 으로 유지하는 비용이 크고, 위의 어느 칼럼도 그 영역을 다루지 않기 때문입니다. C# → TS 메서드 transpile 의 가장 가까운 시도였던 `Rosetta` (andry-tino) 는 README 에 "the project is dead" 라고 명시되어 있습니다. 왜 멈췄는지에서 얻은 교훈이 Mirrorgen 의 설계에 반영되어 있으며 [`docs/CONCEPT_ko.md`](docs/CONCEPT_ko.md) 에 정리되어 있습니다.

## 의도적으로 안 하는 것

Mirrorgen 은 *부분집합* transpiler 입니다. 임의의 C# 을 변환하려고 시도하지 **않습니다**. 출력이 예측 가능하고, 디버깅 가능하고, byte-exact 로 유지되도록 지원 범위를 의도적으로 좁게 정의합니다:

- ❌ `async` / `await`, `Task`, threading
- ❌ LINQ, deferred enumerable
- ❌ `Span<T>`, `ref`, `unsafe`, pointer
- ❌ Exception (대신 result type 으로 반환)
- ❌ Reflection
- ❌ Inheritance, virtual dispatch

위 한계를 넘는 `[Transpile]` 멤버는 Roslyn analyzer 가 빌드 에러로 표시합니다 — codegen 시점이 아니라 IDE 에서 즉시 알 수 있습니다.

정확한 subset spec 과 roadmap 은 [`docs/CONCEPT_ko.md`](docs/CONCEPT_ko.md) 를 보세요.

## 상태

NuGet 에 5개 패키지 모두 배포됨 (현재 버전은 위 배지 참고). walker subset 이
feature-complete, plugin discovery 가 end-to-end 동작, cross-validation
harness 가 매 push 마다 C# ↔ TS byte-equivalence 를 검증합니다. 얼리 어답터
피드백을 받는 동안 API 는 변경될 수 있습니다.

새 릴리스 알림을 받고 싶으면 watch / star 해주세요.

## 프로젝트에 통합하는 방식

```bash
# YourProject.Rules 에서 — 항상 최신 배포 버전을 가져옵니다
dotnet add package Mirrorgen.Attributes
dotnet add package Mirrorgen.Analyzers
dotnet add package Mirrorgen.MSBuild
```

`Mirrorgen.Analyzers` 와 `Mirrorgen.MSBuild` 는 빌드 시점 전용이므로
`PrivateAssets="all"` 을 붙여 소비 프로젝트로 전파되지 않게 합니다. 그런 다음
출력 경로와 config 를 지정합니다:

```xml
<!-- YourProject.Rules.csproj — `dotnet add package` 가 방금 만든 항목에
     PrivateAssets 를 추가합니다 (Version 속성은 그대로 유지) -->
<ItemGroup>
    <PackageReference Include="Mirrorgen.Analyzers" PrivateAssets="all" />
    <PackageReference Include="Mirrorgen.MSBuild" PrivateAssets="all" />
</ItemGroup>

<PropertyGroup>
    <MirrorgenOutput>$(MSBuildThisFileDirectory)../YourProject.Client/src/_generated/</MirrorgenOutput>
    <MirrorgenConfig>YourProject.MirrorgenConfig</MirrorgenConfig>
</PropertyGroup>
```

```csharp
// 도메인 타입 매핑 plugin
public sealed class MirrorgenConfig : IMirrorgenExtension
{
    public void Configure(IMirrorgenBuilder b)
    {
        b.MapType<OrderId>().AsPrimitive("number");
        b.MapType<Money>().RuntimeImport("Money");
    }
}
```

TypeScript 쪽은 정수 산술 / equality / pluggable 도메인 helper 를 담은 작은 runtime 패키지(`@mirrorgen/runtime`)를 받습니다. 몇 KB 이하로 유지되어 generated 코드가 깨끗하게 유지됩니다.

## License

MIT.

## 저장소 구조

```
mirrorgen/
  src/
    Mirrorgen.Core/         # Roslyn 기반 transpiler 엔진
    Mirrorgen.Attributes/   # [Transpile], [GenerateCrossTest] — 의존성 0, 사용자가 ref
    Mirrorgen.Analyzers/    # subset 강제 Roslyn analyzer
    Mirrorgen.MSBuild/      # MSBuild target wrapper
    Mirrorgen.Cli/          # `dotnet mirrorgen` 도구
  runtime-ts/               # @mirrorgen/runtime npm 패키지
    cross/                  # cross-validation 용 TS emit + fixture JSON (커밋됨)
  cross-fixtures/           # cross-validation 흐름이 소비하는 C# subject 메서드
  scripts/
    regen-cross.sh          # runtime-ts/cross/{subject.ts,subject.fixtures.json} 재생성
  samples/
    minimal/                # 최소 C# → TS 예제
    pricing-rules/          # 도메인 타입 매핑이 포함된 비자명한 예제
  tests/
    Mirrorgen.Tests/        # walker / operator / fixture-generator 단위 테스트
  docs/
    CONCEPT.md / CONCEPT_ko.md   # 설계 문서 (영문 / 국문)
    SUBSET.md                    # 정확한 subset spec
    ROADMAP.md
```
