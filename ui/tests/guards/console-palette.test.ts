import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { describe, expect, it } from 'vitest';

// The console's palette for the browser shell's screens is one small sheet, scoped to the console's light theme. The
// classic shell and the dark theme keep the shell's own tokens, so the sheet may match neither.

const src = join(import.meta.dirname, '..', '..', 'src');
const sheetPath = join(src, 'console', 'harness-palette.css');
const sheet = readFileSync(sheetPath, 'utf8');
const scope = ":root[data-theme='light'] .jc-console .harness";

function files(dir: string): string[] {
    return readdirSync(dir).flatMap((name) => {
        const path = join(dir, name);
        return statSync(path).isDirectory() ? files(path) : [path];
    });
}

describe('the console palette sheet', () => {
    it('has two rules, whose selectors are the console light scope and nothing wider', () => {
        const selectors = [...sheet.replace(/\/\*[\s\S]*?\*\//g, '').matchAll(/([^{}]+)\{/g)].map((m) => m[1]!.trim());
        expect(selectors).toEqual([
            scope,
            `${scope} .settings .voice-part input,
${scope} .settings .voice-part select,
${scope} .settings .voice-part textarea`,
        ]);
    });

    it('is loaded by the console mount alone, so the classic shell never receives it', () => {
        const users = files(src)
            .filter((file) => /\.(tsx?|css)$/.test(file) && readFileSync(file, 'utf8').includes('harness-palette.css'))
            .map((file) => relative(src, file).split(sep).join('/'));
        expect(users).toContain('console/mount.tsx');
        expect(users.filter((file) => file !== 'console/mount.tsx' && file !== 'console/harness-palette.css')).toEqual(
            [],
        );
        expect(readFileSync(join(src, 'classic.tsx'), 'utf8')).not.toContain('harness-palette');
    });

    it('sets the values the ruling names', () => {
        for (const line of [
            '--text-base: 14px;',
            '--text: var(--ink);',
            '--text-secondary: var(--mute);',
            '--primary-bg: var(--teal);',
            '--radius: 9px;',
            '--radius-panel: 14px;',
            '--danger: var(--red);',
            'background: #f6f8fc;',
            '--border-strong: var(--line);',
            '--primary-hover: #3261f0;',
            '--accent-hover: #3261f0;',
            'border-radius: var(--radius-sm);',
        ])
            expect(sheet).toContain(line);
    });

    it('keeps the six panel rules of the classic shell as they were, and sets --radius-panel nowhere else', () => {
        const css = (name: string) => readFileSync(join(src, 'styles', name), 'utf8');
        const app = css('app.css');
        const components = css('components.css');
        const rules: [string, string, string][] = [
            [app, '.card', '8px'],
            [components, '.preview', 'var(--radius)'],
            [components, '.dialog', '8px'],
            [components, '.thread', 'var(--radius)'],
            [components, '.timeline .msgs', 'var(--radius)'],
            [components, '.tile', '8px'],
        ];
        for (const [text, selector, old] of rules) {
            const start = text.indexOf('\n' + selector + ' {');
            expect(start, selector).toBeGreaterThanOrEqual(0);
            const body = text.slice(start, text.indexOf('}', start));
            expect(body, selector).toContain(`border-radius: var(--radius-panel, ${old});`);
        }
        const setters = files(src).filter(
            (file) => /\.css$/.test(file) && /--radius-panel\s*:/.test(readFileSync(file, 'utf8')),
        );
        expect(setters.map((file) => relative(src, file).split(sep).join('/'))).toEqual([
            'console/harness-palette.css',
        ]);
    });
});
