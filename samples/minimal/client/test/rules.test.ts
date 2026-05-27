import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import * as rules from '../src/_generated/Pricing.ts';

interface FixtureCall {
    args: unknown[];
    expected: unknown;
}

interface FixtureRecord {
    name: string;
    calls: FixtureCall[];
}

function decodeBigInts(v: unknown): unknown {
    if (typeof v === 'string' && v.startsWith('__bigint:')) {
        return BigInt(v.slice('__bigint:'.length));
    }
    if (Array.isArray(v)) return v.map(decodeBigInts);
    if (v !== null && typeof v === 'object') {
        const out: Record<string, unknown> = {};
        for (const [k, val] of Object.entries(v as Record<string, unknown>)) {
            out[k] = decodeBigInts(val);
        }
        return out;
    }
    return v;
}

const here = dirname(fileURLToPath(import.meta.url));
const fixturePath = resolve(here, '../src/_generated/Pricing.fixtures.json');
const fixtures = JSON.parse(readFileSync(fixturePath, 'utf-8')) as FixtureRecord[];

describe('samples/minimal cross-validation (C# Pricing ↔ emitted TS rules)', () => {
    for (const record of fixtures) {
        for (const call of record.calls) {
            const args = call.args.map(decodeBigInts);
            const expected = decodeBigInts(call.expected);
            const argsLabel = args.map((a) => (typeof a === 'bigint' ? `${a}n` : JSON.stringify(a))).join(', ');
            const expectedLabel = typeof expected === 'bigint' ? `${expected}n` : JSON.stringify(expected);
            it(`${record.name}(${argsLabel}) === ${expectedLabel}`, () => {
                const fn = (rules as Record<string, unknown>)[record.name];
                expect(typeof fn).toBe('function');
                const result = (fn as (...args: unknown[]) => unknown)(...args);
                expect(result).toStrictEqual(expected);
            });
        }
    }
});
