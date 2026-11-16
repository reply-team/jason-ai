import { expect, test } from './fixtures';

// A tab no desktop opened, as `jason ui open` opens it: one page of text and the Demo | Live switch, never the console,
// at a wide window and at a phone's width.
for (const size of [
    { name: 'wide', width: 1280, height: 800 },
    { name: 'narrow', width: 390, height: 844 },
]) {
    test(`a tab with no desktop shows one page and no console, ${size.name}`, async ({ page, jason }) => {
        await page.setViewportSize({ width: size.width, height: size.height });
        const { url } = await jason.uiOpen('--approval', 'apr_00000000000000000000000000');
        await page.goto(url);
        const shown = page.locator('.jc-console.no-desktop');
        await expect(shown).toBeVisible();
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
        // The address is kept, and nothing scrolls sideways.
        expect(page.url()).toContain('#/attention/approvals/');
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    });
}
