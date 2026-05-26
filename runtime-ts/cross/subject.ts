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

export function IsWithinDistance(x1: number, y1: number, x2: number, y2: number, radius: number): boolean {
  let dx: number = ((x2 - x1) | 0);
  let dy: number = ((y2 - y1) | 0);
  return ((Math.imul(dx, dx) + Math.imul(dy, dy)) | 0) <= Math.imul(radius, radius);
}
