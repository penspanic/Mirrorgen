import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import * as rules from '../src/_generated/rules.ts';

interface FixtureCall {
    args: unknown[];
    expected: unknown;
}

interface FixtureRecord {
    name: string;
    calls: FixtureCall[];
}

const here = dirname(fileURLToPath(import.meta.url));
const fixturePath = resolve(here, '../src/_generated/rules.fixtures.json');
const fixtures = JSON.parse(readFileSync(fixturePath, 'utf-8')) as FixtureRecord[];

describe('samples/minimal cross-validation (C# Pricing ↔ emitted TS rules)', () => {
    for (const record of fixtures) {
        for (const call of record.calls) {
            const argsLabel = call.args.map((a) => JSON.stringify(a)).join(', ');
            it(`${record.name}(${argsLabel}) === ${JSON.stringify(call.expected)}`, () => {
                const fn = (rules as Record<string, unknown>)[record.name];
                expect(typeof fn).toBe('function');
                const result = (fn as (...args: unknown[]) => unknown)(...call.args);
                expect(result).toStrictEqual(call.expected);
            });
        }
    }
});
