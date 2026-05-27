export function FortyTwo(): number {
  return 42;
}

export function AddSeven(): number {
  return ((35 + 7) | 0);
}

export function Ternary(): number {
  return 5 > 3 ? 10 : 20;
}

export function StrictEquality(): boolean {
  return 1 === 1;
}

export function Greeting(): string {
  return "hi";
}

export function Pi(): number {
  return 3.14;
}

export function AddOne(x: number): number {
  return ((x + 1) | 0);
}

export function Both(a: boolean, b: boolean): boolean {
  return a && b;
}

export function Triple(v: number): number {
  return v * 3;
}

export function Sign(x: number): number {
  if (x > 0) {
    return 1;
  } else if (x < 0) {
    return -1;
  } else {
    return 0;
  }
}

export function Max(a: number, b: number): number {
  if (a > b) {
    return a;
  }
  return b;
}

export function SumTo(n: number): number {
  let top: number = n > 100 ? 100 : (n < 0 ? 0 : n);
  let sum: number = 0;
  for (let i: number = 0; i < top; i++) {
    sum = ((sum + i) | 0);
  }
  return sum;
}

export function Echo(s: string): string {
  return s;
}

export function IncTwice(x: number): number {
  return AddOne(AddOne(x));
}

export function IsWithinDistance(x1: number, y1: number, x2: number, y2: number, radius: number): boolean {
  let dx: number = ((x2 - x1) | 0);
  let dy: number = ((y2 - y1) | 0);
  return ((Math.imul(dx, dx) + Math.imul(dy, dy)) | 0) <= Math.imul(radius, radius);
}

export function MaxThenDouble(a: number, b: number): number {
  return Math.imul(Math.max(a, b), 2);
}

export function Clamp(v: number, lo: number, hi: number): number {
  return Math.min(Math.max(v, lo), hi);
}

export function CategoryByMod(x: number): number {
  let m: number = ((x % 3) | 0);
  if (m < 0) {
    m = ((m + 3) | 0);
  }
  return ((): number => { const _v = m; if (_v === 0) return 100; if (_v === 1) return 200; return 300; throw new Error("switch expression: no arm matched"); })();
}

export function LabelMod4(x: number): string {
  let m: number = ((x % 4) | 0);
  if (m < 0) {
    m = ((m + 4) | 0);
  }
  switch (m) {
    case 0:
      return "zero";
    case 1:
      return "one";
    case 2:
      return "two";
    default:
      return "three";
  }
}

export function CountDownToZero(n: number): number {
  let i: number = n;
  if (i < 0) {
    i = 0;
  }
  if (i > 200) {
    i = 200;
  }
  let steps: number = 0;
  while (i > 0) {
    i--;
    steps++;
  }
  return steps;
}

export function WrapMul(a: number, b: number): number {
  return Math.imul(a, b);
}

export function CountTwoKeys(map: Record<string, number>, a: string, b: string): number {
  let sum: number = 0;
  if ((a in map)) {
    sum = ((sum + map[a]) | 0);
  }
  if ((b in map)) {
    sum = ((sum + map[b]) | 0);
  }
  return sum;
}

export function BuildListAndCount(n: number): number {
  if (n < 0) {
    n = 0;
  }
  if (n > 50) {
    n = 50;
  }
  let xs: number[] = [];
  for (let i: number = 0; i < n; i++) {
    xs.push(i);
  }
  return xs.length;
}

export function WrapAddLong(a: bigint, b: bigint): bigint {
  return BigInt.asIntN(64, a + b);
}

export function WrapMulLong(a: bigint, b: bigint): bigint {
  return BigInt.asIntN(64, a * b);
}

export function FirstNonNegativeStep(n: number): number {
  let i: number = n;
  if (i < -50) {
    i = -50;
  }
  if (i > 50) {
    i = 50;
  }
  do {
    if (i >= 0) {
      break;
    }
    i++;
  } while (i < 100);
  return i;
}
