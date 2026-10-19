import { expect, test } from '@playwright/test';
import { Jason } from './harness';

test('a custom build is served over the default, and deleting it brings the default back', async ({ page }) => {
    const jason = await Jason.start({ build: 'both' });
    try {
        const { url } = await jason.uiOpen();
        await page.goto(url);
        await expect(page.getByRole('navigation')).toBeVisible();
        await page.goto(`${jason.origin}/ui/#/system`);
        const card = page.locator('.card', { hasText: 'This UI' });
        await expect(card).toContainText('custom');
        await expect(card).toContainText('custom-smoke');

        jason.removeBuild('custom');
        await page.reload();
        await expect(page.locator('.card', { hasText: 'This UI' })).toContainText('default-smoke');
    } finally {
        await jason.stop();
        jason.dispose();
    }
});
