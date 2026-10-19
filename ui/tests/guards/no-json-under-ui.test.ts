import { readdirSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

// Guard 2: screen fixtures come from docs/api/fixtures, recorded by the runtime's tests. A .json under ui/ would be a belief.
const root = join(import.meta.dirname, '..', '..');
const allowed = new Set(['package.json', 'package-lock.json', 'tsconfig.json']);
const skipped = new Set(['node_modules', 'dist', 'artifacts', 'test-results', 'playwright-report']);

function jsonFiles(dir: string): string[] {
    const found: string[] = [];
    for (const name of readdirSync(dir)) {
        const path = join(dir, name);
        if (statSync(path).isDirectory()) {
            if (!skipped.has(name)) found.push(...jsonFiles(path));
        } else if (name.endsWith('.json')) {
            found.push(relative(root, path));
        }
    }
    return found;
}

describe('no JSON under ui/', () => {
    it('holds only the three configuration files', () => {
        const unexpected = jsonFiles(root).filter((file) => !allowed.has(file));
        expect(unexpected).toEqual([]);
    });
});
