import { expect, test } from '@playwright/test';
import { Jason } from './harness';

test('with no module, /ui/ is a 404 that says what is missing, and jason ui open explains', async ({ page }) => {
    const jason = await Jason.start({ build: 'none' });
    try {
        const response = await page.request.get(`${jason.origin}/ui/`);
        expect(response.status()).toBe(404);
        const body = await response.text();
        expect(body).toContain('ui_not_installed');
        expect(body).toContain('custom');

        await expect(jason.uiOpen()).rejects.toThrow(/ui_not_installed/);
    } finally {
        await jason.stop();
        jason.dispose();
    }
});
