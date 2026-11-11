import { defineConfig } from 'vitest/config';
import babel from '@rolldown/plugin-babel';
// @ts-expect-error The package's "types" entry names a file it does not ship.
import signalsTransform from '@preact/signals-react-transform';
import { buildManifest, buildNotices } from './scripts/package-lib.mjs';
import { API_VERSION } from './src/api/version';
import { scopePlugin } from './src/console/scope';

// base: the runtime serves the build under /ui/. assetsInlineLimit 0: the page runs under
// `default-src 'self'`, and an inlined data: URI would be refused by the browser.
export default defineConfig({
    base: '/ui/',
    plugins: [
        // A component re-renders when a signal it read changes. Preact's signals did that on their own; React needs each
        // component wrapped, which this transform does at build time. Mode all wraps every component, not only those
        // that read `.value` themselves, so one that reads a signal through a helper re-renders as it did under Preact.
        babel({ plugins: [[signalsTransform, { mode: 'all' }]] }),
        buildManifest({ version: process.env.JASON_VERSION ?? '0.0.0-local', apiVersion: API_VERSION }),
        buildNotices(),
    ],
    // The screen tests import the two published prompt texts under docs/prompts as raw text (`?raw`). That folder is
    // outside this project, and without this entry vitest refuses it ("Denied ID ... docs/prompts/approval.txt?raw"): the
    // list names the project and that one folder, and nothing wider.
    // The console shows the browser shell's screens in a panel; their stylesheet is scoped to that panel here, at build
    // time, because the page may not inject one.
    css: { postcss: { plugins: [scopePlugin('console/harness.css', '.harness')] } },
    server: { fs: { allow: ['.', '../docs/prompts'] } },
    build: { outDir: 'dist', emptyOutDir: true, assetsInlineLimit: 0, sourcemap: false },
    test: {
        projects: [
            {
                extends: true,
                test: { name: 'unit', environment: 'node', include: ['src/**/*.test.ts', 'tests/guards/**/*.test.ts'] },
            },
            {
                extends: true,
                test: {
                    name: 'screen',
                    environment: 'happy-dom',
                    include: ['src/**/*.screen.test.tsx'],
                    setupFiles: ['src/test/setup.ts'],
                },
            },
        ],
    },
});
