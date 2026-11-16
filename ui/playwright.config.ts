import { defineConfig } from '@playwright/test';

// The sealed probe, a hostile page in the console's frame, runs in the pull request's CI alone, where the workflow sets
// JASON_SEALED_PROBE; there it runs in Chromium, Firefox and WebKit, and every other spec in Chromium.
const probe = /probe[\\/].*\.spec\.ts$/;
const probing = process.env.JASON_SEALED_PROBE === '1';

export default defineConfig({
    testDir: 'tests/e2e',
    timeout: 60_000,
    workers: 2,
    reporter: 'list',
    globalSetup: './tests/e2e/global-setup.ts',
    use: { headless: true },
    projects: [
        { name: 'chromium', use: { browserName: 'chromium' }, ...(probing ? {} : { testIgnore: probe }) },
        ...(probing
            ? [
                  { name: 'firefox', use: { browserName: 'firefox' as const }, testMatch: probe },
                  { name: 'webkit', use: { browserName: 'webkit' as const }, testMatch: probe },
              ]
            : []),
    ],
});
