import { defineConfig } from '@playwright/test';

export default defineConfig({
    testDir: 'tests/e2e',
    timeout: 60_000,
    workers: 2,
    reporter: 'list',
    globalSetup: './tests/e2e/global-setup.ts',
    use: { browserName: 'chromium', headless: true },
});
