# Mirrorgen — 부분집합 명세

[English](SUBSET.md) · **한국어**

v0.1 transpiler 가 받아들이는 정확한 명세. 여기 나열되지 않은 것은 미지원 — emit 시점에 `NotSupportedException` 으로 실패하거나, `Mirrorgen.Analyzers` 의 analyzer 들이 먼저 빌드 시점에 차단합니다.

이 부분집합은 의도적으로 좁게 잡혀 있어 — emit 결과가 예측 가능하고, debugging 가능하며, C# / TypeScript 양쪽 boundary 에서 byte-exact 유지.

상위 차원의 근거는 [`CONCEPT_ko.md`](CONCEPT_ko.md). 이 문서는 enumeration.

## Type surface (v0.1)

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

## Expression / statement surface (v0.1)

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

## Analyzer enforcement

`[Transpile]` 멤버의 subset 위반은 빌드 에러로 노출 — 안정된 diagnostic id 사용:

| Id      | Severity | 무엇을 잡나                                                                              |
|---------|----------|-------------------------------------------------------------------------------------------|
| MG0001  | Error    | LINQ (모든 `System.Linq` 호출)                                                            |
| MG0002  | Error    | `async` / `await` / `Task` / `ValueTask`                                                  |
| MG0003  | Error    | `Span<T>` / `ReadOnlySpan<T>` / `ref` / `in` / `out` / `unsafe` / pointer type             |
| MG0004  | Error    | `throw`                                                                                   |
| MG0005  | Error    | `System.Reflection.*`, `System.Type` / `System.Activator` 호출, `typeof(...)`              |
| MG0006  | Error    | `[Transpile]` 메서드를 가진 class 가 `object` 외의 base 상속                              |

`MG0001`..`MG0099` 범위의 diagnostic id 는 stable. `#pragma warning disable` 로 suppress 가능하지만 권장하지 않음 — suppress 된 경우 Mirrorgen 은 emit 정확성을 보장하지 않습니다.
