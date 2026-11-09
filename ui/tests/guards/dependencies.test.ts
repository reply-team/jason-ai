import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

// Guard 3: the counterpart of the runtime's "no vendor name in runtime C#". A mocking library, a component library
// or a second framework arrives through this list or not at all. The framework is React with its DOM renderer, and
// signals for React; the toolchain adds what React needs and Preact did not: the Babel transform that makes a component
// re-render on the signals it reads (with Babel itself and the plugin that runs it in the build), React's test renderer
// with the DOM queries it takes as a peer, and React's types. Vite compiles the JSX itself, so no React plugin for it.
// dockview-react is the desktop console's one layout library: the chat and the panels beside it docked, split and
// resized by the person, which the console would otherwise have to write itself. It pulls its core in with it.
const manifest = JSON.parse(readFileSync(join(import.meta.dirname, '..', '..', 'package.json'), 'utf8')) as {
    dependencies: Record<string, string>;
    devDependencies: Record<string, string>;
};

const runtime = ['@fontsource-variable/inter', '@preact/signals-react', 'dockview-react', 'react', 'react-dom'];
const build = [
    '@babel/core',
    '@eslint/js',
    '@playwright/test',
    '@preact/signals-react-transform',
    '@rolldown/plugin-babel',
    '@testing-library/dom',
    '@testing-library/react',
    '@types/node',
    '@types/react',
    '@types/react-dom',
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
    it('runtime dependencies are exactly React, Signals, the font and the dock', () => {
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
