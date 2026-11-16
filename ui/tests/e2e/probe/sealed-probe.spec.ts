import { copyFileSync } from 'node:fs';
import { join } from 'node:path';
import type { Page } from '@playwright/test';
import { expect, test } from '../fixtures';
import type { Jason } from '../harness';

// The seal in the browser, tried from inside: a hostile page stands where the demo page does, the console shows it in its
// frame, and its script tries every way out it can name. Every try must come back refused, the console above it must be
// as it was, and nothing the page asks for may reach an address. The probe runs in the pull request's CI alone, in each
// engine its workflow installs (playwright.config.ts).

const probe = join(import.meta.dirname, 'sealed-probe.html');

/** Every try the probe makes, in the order it writes them. */
const tries = [
    'origin',
    'localStorage',
    'sessionStorage',
    'cookie',
    'parent.document',
    'parent.localStorage',
    'parent.location',
    'top.location',
    'window.open',
    'alert',
    'download',
    'postMessage',
];
const awaited = [
    'indexedDB',
    'fetch API',
    'fetch API with a bearer',
    'fetch journal stream',
    'fetch pairing door',
    'fetch bridge',
    'fetch outside',
    'fullscreen',
    'camera',
    'clipboard',
    'websocket',
    'image outside',
    'form to the pairing door',
];

/** The probe placed where the build's demo page stands: a custom build, served over the default, with the probe in it. */
function placeProbe(jason: Jason): void {
    jason.placeBuild('custom', 'probe');
    copyFileSync(probe, join(jason.dataDir, 'ui', 'custom', 'sealed', 'demo.html'));
}

/** What the probe wrote: each try with its outcome and its own words. */
async function outcomes(page: Page, frame = true): Promise<[string, string, string][]> {
    const root = frame ? page.frameLocator('iframe.demo-frame').locator('body') : page.locator('body');
    await expect(root).toHaveAttribute('data-done', '1', { timeout: 20_000 });
    return root
        .locator('li')
        .evaluateAll((items) =>
            items.map((item) => [
                item.getAttribute('data-try') ?? '',
                item.getAttribute('data-outcome') ?? '',
                item.textContent ?? '',
            ]),
        );
}

test('a page under sealed/, in the console’s frame, reaches nothing and changes nothing above it', async ({
    page,
    jason,
}) => {
    placeProbe(jason);
    const dialogs: string[] = [];
    const popups: string[] = [];
    const downloads: string[] = [];
    const reached: string[] = [];
    page.on('dialog', (dialog) => {
        dialogs.push(dialog.message());
        void dialog.dismiss();
    });
    page.on('popup', (popup) => popups.push(popup.url()));
    page.on('download', (download) => downloads.push(download.suggestedFilename()));
    page.on('requestfinished', (request) => {
        if (request.frame().parentFrame() !== null) reached.push(request.url());
    });

    const { url } = await jason.uiOpen();
    await page.goto(url);
    await expect(page.locator('.jc-console .side')).toBeVisible();
    const token = await page.evaluate(() => localStorage.getItem('jason.ui.session'));
    expect(token).not.toBeNull();
    await page.getByRole('group', { name: 'Data' }).getByRole('button', { name: 'Demo' }).click();
    await expect(page.locator('iframe.demo-frame')).toHaveAttribute('sandbox', 'allow-scripts');

    const written = await outcomes(page);
    expect(written.map(([name]) => name).sort()).toEqual([...tries, ...awaited].sort());
    expect(written.filter(([, outcome]) => outcome !== 'refused')).toEqual([]);

    // The page above is the console's, where it was, holding what it held.
    expect(page.url()).toMatch(/\/ui\/#\/demo$/);
    expect(await page.evaluate(() => localStorage.getItem('jason.ui.session'))).toBe(token);
    await expect(page.locator('.demo-over')).toBeVisible();
    expect(dialogs).toEqual([]);
    expect(popups).toEqual([]);
    expect(downloads).toEqual([]);

    // The frame moving itself: to an outside address, and to the console's own page, which never shows in a frame.
    const inside = page.frames().find((frame) => frame.parentFrame() !== null)!;
    await inside.evaluate(() => location.assign('https://outside.invalid/')).catch(() => undefined);
    await page.waitForTimeout(1_500);
    for (const frame of page.frames()) expect(frame.url()).not.toContain('outside.invalid');
    const again = page.frames().find((frame) => frame.parentFrame() !== null);
    if (again !== undefined && again.url().endsWith('/ui/sealed/demo.html')) {
        await again.evaluate(() => location.assign('/ui/')).catch(() => undefined);
        await page.waitForTimeout(1_500);
    }
    await expect(page.frameLocator('iframe.demo-frame').locator('.jc-console')).toHaveCount(0);
    expect(page.url()).toMatch(/\/ui\/#\/demo$/);

    // Nothing the frame asked for reached an address but its own page.
    expect(reached.filter((each) => !each.startsWith('data:'))).toEqual([`${jason.origin}/ui/sealed/demo.html`]);
});

test('a page under sealed/, opened by itself, has an origin of its own and reaches nothing', async ({
    page,
    jason,
}) => {
    placeProbe(jason);
    const reached: string[] = [];
    page.on('requestfinished', (request) => reached.push(request.url()));
    await page.goto(`${jason.origin}/ui/sealed/demo.html`);

    const written = await outcomes(page, false);
    // Opened by itself the page is its own parent and top, so the tries of the page above are no tries here.
    const ownTries = written.filter(([name]) => !/^(parent|top)\.|^postMessage$/.test(name));
    expect(ownTries.filter(([, outcome]) => outcome !== 'refused')).toEqual([]);
    expect(ownTries.map(([name]) => name)).toContain('origin');
    expect(reached.filter((each) => !each.startsWith('data:'))).toEqual([`${jason.origin}/ui/sealed/demo.html`]);
});
