# Mirrorgen — 부분집합 명세

[English](SUBSET.md) · **한국어**

v0.1 transpiler 가 받아들이는 정확한 명세. 여기 나열되지 않은 것은 미지원 — emit 시점에 `NotSupportedException` 으로 실패하거나, `Mirrorgen.Analyzers` 의 analyzer 들이 먼저 빌드 시점에 차단합니다.

이 부분집합은 의도적으로 좁게 잡혀 있어 — emit 결과가 예측 가능하고, debugging 가능하며, C# / TypeScript 양쪽 boundary 에서 byte-exact 유지.

상위 차원의 근거는 [`CONCEPT_ko.md`](CONCEPT_ko.md). 이 문서는 enumeration.

## Type surface (v0.1)

지원:
- `enum` (int-backed)
- `record` (positional 또는 property-init)
- `class` 와 `struct` — property (get-only, init-only, `get; set;`) 및 public field
- Primitive: `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `float`, `double`, `string`, `long` / `ulong` (TS `bigint` 으로 emit, `BigInt.asIntN(64, ...)` / `asUintN(64, ...)` wrap)
- `T[]`, `List<T>`, `IReadOnlyList<T>`, `IList<T>` (모두 `T[]` 로 emit)
- `Dictionary<K,V>`, `IReadOnlyDictionary<K,V>`, `IDictionary<K,V>` (`Record<K,V>` 로 emit, K 는 string-coercible)
- `Nullable<T>` (`T | null` 로 emit)
- Transitive reachability — marked type 에서 도달 가능한 leaf type 은 자체 attribute 불필요
- Plugin remapping (`IMirrorgenExtension`) — 사용자 정의 plugin 1개가 C# 도메인 primitive (예: `record OrderId(int Value)`) 를 TS primitive 또는 runtime-import 이름으로 매핑. 매핑된 type 은 emit 되지 않고 cross-test fixture 값도 내부 field 만 unwrap.

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
  - `uint` → `>>> 0` 으로 wrap (`/` 와 `>>` 를 포함한 모든 연산자)
  - `short` / `ushort` / `byte` / `sbyte` → 명시적 mask 로 wrap
  - int / uint 곱셈 → `Math.imul` (JS Number 정밀도 손실 회피)
  - `long` / `ulong` → `BigInt`, `BigInt.asIntN(64, …)` / `asUintN(64, …)` 로 wrap.
    시프트 카운트는 C# 과 맞추어 6비트로 마스크
- Boolean operator, 비교
- `if` / `else`
- `for` (C-style), `foreach` (`T[]`, `List<T>`, `IReadOnlyList<T>`, `IList<T>`)
- `while` / `do-while` (`break` / `continue` 포함)
- `switch` statement (constant + enum-member pattern), `switch` expression (constant, enum-member, relational `> < >= <= == !=`, 괄호, `and` / `or`, `_`, `when` 가드)
- 비트 연산자 `& | ^ ~`, 시프트 `<< >>`, C# 11 부호 없는 우시프트 `>>>`
- 위 전부에 대한 복합 대입 (`+= -= *= /= %= &= |= ^= <<= >>= >>>=`)
- `ref` / `out` / `in` 파라미터 (호출부에서 튜플 구조분해로 emit)
- Generic method (`T Identity<T>(T x)`) — type constraint 없이
- `switch` expression 의 discard arm 에 놓인 `throw` — 전체성(totality) 표시로서
- 같은 프로젝트의 다른 `[Transpile]` 메서드 호출
- 허용 리스트의 `System.Math.*` / `System.MathF.*` 함수 호출
- `return`

v0.1 에 지원 안 함:
- 모든 형태의 LINQ
- `goto`
- `yield return`
- `using`, `try` / `catch` / `finally`
- Type pattern (`int n when …`), positional / property / list pattern, 재귀 pattern
- 사용자 타입에 대한 operator overloading
- Generic method 의 type constraint
- 도달 가능한 제어 흐름으로서의 `throw` (MG0004 참조)
- `long` / `ulong` 에 대한 `>>>` — JS `BigInt` 에 부호 없는 우시프트가 없다

## Analyzer enforcement

`[Transpile]` 멤버의 subset 위반은 빌드 에러로 노출 — 안정된 diagnostic id 사용:

| Id      | Severity | 무엇을 잡나                                                                              |
|---------|----------|-------------------------------------------------------------------------------------------|
| MG0001  | Error    | LINQ (모든 `System.Linq` 호출)                                                            |
| MG0002  | Error    | `async` / `await` / `Task` / `ValueTask`                                                  |
| MG0003  | Error    | `Span<T>` / 모든 `ref struct` / `ref` 반환 / `unsafe` / pointer type                       |
| MG0004  | Error    | `throw`. 단 `switch` expression 의 discard arm 은 예외                                     |
| MG0005  | Error    | `System.Reflection.*`, `System.Type` / `System.Activator` 호출, `typeof(...)`              |
| MG0006  | Error    | `[Transpile]` 메서드를 가진 class 가 `object` 외의 base 상속                              |

### 왜 `ref` / `out` 은 들어오고 `throw` 는 빠지는가

둘 다 "JavaScript 에 없는 C# 문법" 처럼 보이지만, 가르는 기준은
[`CONCEPT_ko.md`](CONCEPT_ko.md) 가 다른 모든 곳에 적용하는 것과 같다 —
**의미가 깨끗하게 round-trip 하는가, 그리고 cross-validation 으로 증명할 수 있는가.**

`ref` / `out` / `in` **파라미터**는 통과한다. 완전히 지역적이고, 튜플 구조분해로
emit 되며, 값은 값 그대로다. 비트 수준에서 애매해질 여지가 없다.
(`ref` **반환**은 통과하지 못한다 — 호출자의 저장소를 별칭하는 것이라 JS 에 대응이 없다.)

`throw` 는 양쪽 다 실패한다. 예외 타입 매핑이 손실이 있다 — 아는 타입 둘만
`RangeError` / `TypeError` 로 가고 나머지는 전부 `Error` 로 뭉개져서, C# 의
`catch (SomeSpecificException)` 과 JS 의 `instanceof` 검사가 같은 집합을 고르지
않는다. 그리고 던지는 경로는 cross-validation 자체가 불가능하다 — fixture 캡처는
메서드를 호출해 반환값을 기록하므로, 던지는 입력은 fixture 행을 만들지 못한다.

`switch` expression 의 discard arm 만 예외다. 그 자리의 throw 는 동작을 기술하는
것이 아니라 switch 가 전체(total)임을 주장하는 것이고, 도달하면 그 자체가 버그이므로
fixture 가 이견을 가질 대상이 없다. Mirrorgen 도 매칭되는 arm 이 없을 때의 안전망을
정확히 같은 자리에 throw 로 생성한다.

`MG0001`..`MG0099` 범위의 diagnostic id 는 stable. `#pragma warning disable` 로 suppress 가능하지만 권장하지 않음 — suppress 된 경우 Mirrorgen 은 emit 정확성을 보장하지 않습니다.

## Cross-test attribute

- `[GenerateCrossTest(Samples = N, Seed = S)]` — 메서드 당 N 개 random sample, seed 로 재현. fixture row 생성의 필요 조건.
- `[CrossTestCase(values...)]` — 명시적 input 한 줄을 random sample 옆에 추가. 중첩 가능 (여러 attribute 모두 소비). 인자 수가 메서드 parameter 수와 일치해야 하고, 값은 C# attribute constant 여야. random sampling 이 거의 못 잡는 corner — `int.MinValue`, `0`, `int.MaxValue`, 과거 버그를 발생시킨 값 — 에 적합.
