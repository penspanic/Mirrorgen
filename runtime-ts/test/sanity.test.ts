import { describe, it, expect } from 'vitest';
import { i32, u32, u16, u8, imul } from '../src/index.js';

describe('integer wrappers', () => {
    it('i32 keeps in range', () => {
        expect(i32(0x12345678)).toBe(0x12345678);
    });

    it('i32 wraps to negative on high bit', () => {
        expect(i32(0x80000000)).toBe(-2147483648);
    });

    it('u32 unwraps negative to high bit', () => {
        expect(u32(-1)).toBe(0xffffffff);
    });

    it('u16 masks high bits', () => {
        expect(u16(0x12345)).toBe(0x2345);
    });

    it('u8 masks high bits', () => {
        expect(u8(0x1ff)).toBe(0xff);
    });

    it('imul preserves int32 truncation', () => {
        expect(imul(0x10000, 0x10000)).toBe(0);
    });
});
