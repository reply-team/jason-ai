import { expect, test } from '@playwright/test';
import { Jason, freePort } from './harness';

test('a bookmark works after the runtime restarts on its fixed port', async ({ page }) => {
    const port = await freePort();
    const jason = await Jason.start({ port });
    try {
        const { url } = await jason.uiOpen();
        await page.goto(url);
        await expect(page.getByRole('navigation')).toBeVisible();

        await jason.restart();
        expect(jason.port).toBe(port);
        await page.goto(`${jason.origin}/ui/#/attention`);
        await expect(page.getByRole('navigation')).toBeVisible();
        await expect(page.getByText(/isn't paired/)).toHaveCount(0);
    } finally {
        await jason.stop();
        jason.dispose();
    }
});
