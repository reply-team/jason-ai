import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, test } from './fixtures';

// The demo in a browser tab, over the build the release carries: the page under sealed/ is the desktop's own, byte for
// byte, and in its frame it runs with no error and asks for nothing but its own document. This is the drop check: a new
// page that reaches for an address, or breaks without storage, fails it here.

const desktopPage = resolve(import.meta.dirname, '..', '..', '..', 'desktop', 'demo', 'index.html');
const sha256 = (bytes: Buffer) => createHash('sha256').update(bytes).digest('hex');

test('the build serves the desktop’s demo page as it is, under the sealed set', async ({ jason, request }) => {
    const answer = await request.get(`${jason.origin}/ui/sealed/demo.html`);
    expect(answer.status()).toBe(200);
    expect(sha256(await answer.body())).toBe(sha256(readFileSync(desktopPage)));
    expect(answer.headers()['content-security-policy']).toMatch(/; frame-ancestors 'self'; sandbox allow-scripts$/);
});

test('the demo opens in the tab’s frame by itself, asks for nothing but its own page, and Live brings the page back', async ({
    page,
    jason,
}) => {
    const problems: string[] = [];
    const asked: string[] = [];
    page.on('pageerror', (error) => problems.push(`pageerror: ${error.message}`));
    page.on('console', (message) => {
        if (message.type() === 'error' || /Content Security Policy|Refused to/.test(message.text()))
            problems.push(`${message.type()}: ${message.text()}`);
    });
    page.on('request', (each) => {
        if (each.frame().parentFrame() !== null) asked.push(each.url());
    });

    const { url } = await jason.uiOpen();
    await page.goto(url);
    // No desktop and nothing waiting: the tab opens on the Demo by itself.
    await expect(page).toHaveURL(/#\/demo$/);
    const frame = page.locator('iframe.demo-frame');
    await expect(frame).toBeVisible();
    await expect(frame).toHaveAttribute('sandbox', 'allow-scripts');
    const inside = page.frameLocator('iframe.demo-frame');
    await expect(inside.locator('body')).not.toBeEmpty();
    // Long enough for the page's own start to have run and failed, if it fails without storage.
    await page.waitForTimeout(2_000);

    expect(asked.filter((each) => !each.startsWith('data:'))).toEqual([`${jason.origin}/ui/sealed/demo.html`]);
    expect(problems).toEqual([]);

    await page.getByRole('group', { name: 'Data' }).getByRole('button', { name: 'Live' }).click();
    await expect(page.locator('.jc-console.no-desktop')).toBeVisible();
    await expect(page.locator('.jc-console .side')).toHaveCount(0);
    await expect(frame).toHaveCount(0);
});
