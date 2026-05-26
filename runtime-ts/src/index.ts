export function i32(value: number): number {
    return value | 0;
}

export function u32(value: number): number {
    return value >>> 0;
}

export function u16(value: number): number {
    return value & 0xffff;
}

export function u8(value: number): number {
    return value & 0xff;
}

export function imul(a: number, b: number): number {
    return Math.imul(a, b);
}
