import { expect, test } from '@playwright/test';
import { quietDesktop } from './fixtures';
import { Jason, freePort } from './harness';

test('a bookmark works after the runtime restarts on its fixed port', async ({ page }) => {
    // The console is the window's; a plain tab draws one message.
    await page.addInitScript(quietDesktop);
    const port = await freePort();
    const jason = await Jason.start({ port });
    try {
        const { url } = await jason.uiPair();
        await page.goto(url);
        await expect(page.locator('.jc-console .side')).toBeVisible();

        await jason.restart();
        expect(jason.port).toBe(port);
        await page.goto(`${jason.origin}/ui/#/attention`);
        await expect(page.locator('.jc-console .side')).toBeVisible();
        await expect(page.getByText(/isn't paired/)).toHaveCount(0);
    } finally {
        await jason.stop();
        jason.dispose();
    }
});
