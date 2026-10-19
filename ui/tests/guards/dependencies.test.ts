import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

// Guard 3: the counterpart of the runtime's "no vendor name in runtime C#". A mocking library, a component library
// or a second framework arrives through this list or not at all.
const manifest = JSON.parse(readFileSync(join(import.meta.dirname, '..', '..', 'package.json'), 'utf8')) as {
    dependencies: Record<string, string>;
    devDependencies: Record<string, string>;
};

const runtime = ['@fontsource-variable/inter', '@preact/signals', 'preact'];
const build = [
    '@eslint/js',
    '@playwright/test',
    '@preact/preset-vite',
    '@testing-library/preact',
    '@types/node',
    '@types/yauzl',
    '@types/yazl',
    'eslint',
    'happy-dom',
    'prettier',
    'typescript',
    'typescript-eslint',
    'vite',
    'vitest',
    'yauzl',
    'yazl',
];

describe('dependencies', () => {
    it('runtime dependencies are exactly Preact, Signals and the font', () => {
        expect(Object.keys(manifest.dependencies).sort()).toEqual(runtime);
    });
    it('build dependencies are exactly the toolchain', () => {
        expect(Object.keys(manifest.devDependencies).sort()).toEqual(build);
    });
    it('every version is exact', () => {
        for (const version of [...Object.values(manifest.dependencies), ...Object.values(manifest.devDependencies)]) {
            expect(version).toMatch(/^\d+\.\d+\.\d+/);
        }
    });
});
