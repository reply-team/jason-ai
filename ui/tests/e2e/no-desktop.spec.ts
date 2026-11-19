import { expect, test } from './fixtures';

// A tab no desktop opened, as the link `jason ui pair` prints opens it: with nothing waiting it opens on the Demo; Live shows one page of
// text and the Demo | Live switch, never the console, at a wide window and at a phone's width.
for (const size of [
    { name: 'wide', width: 1280, height: 800 },
    { name: 'narrow', width: 390, height: 844 },
]) {
    test(`a tab with no desktop opens on the demo, and Live shows one page and no console, ${size.name}`, async ({
        page,
        jason,
    }) => {
        await page.setViewportSize({ width: size.width, height: size.height });
        const { url } = await jason.uiPair('--approval', 'apr_00000000000000000000000000');
        await page.goto(url);
        // Nothing waits, so the tab opens on the Demo by itself, and the address says so.
        await expect(page.locator('iframe.demo-frame')).toBeVisible();
        await expect(page).toHaveURL(/#\/demo$/);
        const switchButtons = page.getByRole('group', { name: 'Data' }).getByRole('button');
        await expect(switchButtons).toHaveText(['Demo', 'Live']);
        await switchButtons.getByText('Live').click();
        const shown = page.locator('.jc-console.no-desktop');
        await expect(shown).toBeVisible();
        // Pressing Live from the default Demo does not bounce back.
        await page.waitForTimeout(500);
        await expect(shown).toBeVisible();
        await expect(page.locator('iframe.demo-frame')).toHaveCount(0);
        await expect(page.getByRole('heading', { level: 1 })).toHaveText(
            'Live Mode is only available in Jason Desktop.',
        );
        await expect(page.getByRole('link', { name: 'download Jason Desktop' })).toHaveAttribute(
            'href',
            'https://github.com/reply-team/jason-ai/releases',
        );
        await expect(page.getByText('Alternatively, you can build your own web interface')).toBeVisible();
        for (const selector of ['.side', '.shell', '.mode-strip', '.area', 'nav'])
            await expect(page.locator(selector), selector).toHaveCount(0);
        await expect(page.getByRole('group', { name: 'Data' }).getByRole('button')).toHaveText(['Demo', 'Live']);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
        // The person's own choice holds for this tab over a reload, though nothing waits.
        await page.reload();
        await expect(shown).toBeVisible();
        await expect(page.locator('iframe.demo-frame')).toHaveCount(0);
        // And so does Demo.
        await page.getByRole('group', { name: 'Data' }).getByRole('button', { name: 'Demo' }).click();
        await expect(page.locator('iframe.demo-frame')).toBeVisible();
        await page.reload();
        await expect(page.locator('iframe.demo-frame')).toBeVisible();
    });
}
