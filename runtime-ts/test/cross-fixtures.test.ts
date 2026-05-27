import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import * as subject from '../cross/subject.ts';

interface FixtureCall {
    args: unknown[];
    expected: unknown;
}

interface FixtureRecord {
    name: string;
    calls: FixtureCall[];
}

// FixtureGenerator emits long / ulong values as `"__bigint:<value>"` strings
// so they survive the JS round-trip without losing precision above 2^53.
// The harness rehydrates them into TS bigints before invoking the function.
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
const fixturePath = resolve(here, '../cross/subject.fixtures.json');
const fixtures = JSON.parse(readFileSync(fixturePath, 'utf-8')) as FixtureRecord[];

describe('cross-validation (C# subject ↔ emitted TS)', () => {
    for (const record of fixtures) {
        for (const call of record.calls) {
            const args = call.args.map(decodeBigInts);
            const expected = decodeBigInts(call.expected);
            const argsLabel = args.map((a) => (typeof a === 'bigint' ? `${a}n` : JSON.stringify(a))).join(', ');
            const expectedLabel = typeof expected === 'bigint' ? `${expected}n` : JSON.stringify(expected);
            it(`${record.name}(${argsLabel}) === ${expectedLabel}`, () => {
                const fn = (subject as Record<string, unknown>)[record.name];
                expect(typeof fn).toBe('function');
                const result = (fn as (...args: unknown[]) => unknown)(...args);
                expect(result).toStrictEqual(expected);
            });
        }
    }
});
