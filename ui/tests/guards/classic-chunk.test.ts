import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { describe, expect, it } from 'vitest';

// One build, two shells (`src/main.tsx`): the classic shell, which any other browser loads, shares only seven small
// modules of the console with it. Whatever `classic.tsx` imports, directly or through the modules it imports, may not
// reach another file under `src/console/`: the console's Connect, chat and desktop seam belong to its own chunk.

const src = join(import.meta.dirname, '..', '..', 'src');
// `console/state.ts` is the fifth: the send-modes' `open-settings.ts` reads it, and the classic chunk already held it;
// the console's Connect, chat and desktop seam stay out. `console/waiting.ts` and `console/waiting-model.ts` are the
// sixth and seventh: the gate an approval is approved by, read whole first, which the Work items page approves through
// as the chat's cards and Needs you do, so that there is one gate and no copy of it; they import nothing of the console
// but each other and `live.ts`. Dynamic imports (the browser chat tab) are chunks of their own and are not followed.
const shared = [
    'console/html.ts',
    'console/painted.tsx',
    'console/icons.ts',
    'console/live.ts',
    'console/state.ts',
    'console/waiting.ts',
    'console/waiting-model.ts',
];

const specifier =
    /(?:^|\n)\s*(?:import|export)\s+(?!type\b)[^'"`;]*?from\s+['"]([^'"]+)['"]|(?:^|\n)\s*import\s+['"]([^'"]+)['"]/g;

function resolveModule(from: string, target: string): string | null {
    if (!target.startsWith('.')) return null;
    const base = resolve(dirname(from), target);
    for (const candidate of [base, `${base}.ts`, `${base}.tsx`, join(base, 'index.ts'), join(base, 'index.tsx')])
        if (/\.tsx?$/.test(candidate) && existsSync(candidate)) return candidate;
    return null;
}

function closure(entry: string): Set<string> {
    const seen = new Set<string>();
    const queue = [entry];
    while (queue.length > 0) {
        const file = queue.pop()!;
        if (seen.has(file)) continue;
        seen.add(file);
        const text = readFileSync(file, 'utf8');
        for (const match of text.matchAll(specifier)) {
            const next = resolveModule(file, match[1] ?? match[2] ?? '');
            if (next !== null) queue.push(next);
        }
    }
    return seen;
}

describe('the classic shell’s chunk', () => {
    it('reaches no file of the console but the ones it shares', () => {
        const reached = [...closure(join(src, 'classic.tsx'))]
            .filter((file) => !/\.(test|screen\.test)\.tsx?$/.test(file))
            .map((file) => relative(src, file).split(sep).join('/'))
            .filter((file) => file.startsWith('console/'))
            .filter((file) => !shared.includes(file));
        expect(reached).toEqual([]);
    });

    it('sees a console import when there is one', () => {
        const settings = closure(join(src, 'console', 'console.tsx'));
        expect([...settings].some((file) => file.endsWith('connect.ts'))).toBe(true);
    });
});
