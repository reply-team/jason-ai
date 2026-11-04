import { defineConfig } from 'vitest/config';
import preact from '@preact/preset-vite';
import { buildManifest } from './scripts/package-lib.mjs';
import { API_VERSION } from './src/api/version';

// base: the runtime serves the build under /ui/. assetsInlineLimit 0: the page runs under
// `default-src 'self'`, and an inlined data: URI would be refused by the browser.
export default defineConfig({
    base: '/ui/',
    plugins: [
        preact(),
        buildManifest({ version: process.env.JASON_VERSION ?? '0.0.0-local', apiVersion: API_VERSION }),
    ],
    // The screen tests import the two published prompt texts under docs/prompts as raw text (`?raw`). That folder is
    // outside this project, and without this entry vitest refuses it ("Denied ID ... docs/prompts/approval.txt?raw"): the
    // list names the project and that one folder, and nothing wider.
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
