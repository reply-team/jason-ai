import { expect, test } from './fixtures';

// What the screen tests cannot see is where a screen is drawn. These rows measure the boxes in a real browser at a wide
// window: a screen in the console's Page tab fills the panel, and the browser shell keeps its sidebar and gives the
// rest of the width to its content.
const WIDE = { width: 2000, height: 900 };
// A screen is drawn across the panel, not in a column of it: at least this share of the panel's width.
const FILLS = 0.9;

// The desktop's seam as far as the console needs it to mount and show a page: nothing answers, nothing is running.
function quietDesktop(): void {
    const quiet = () => () => undefined;
    const done = () => Promise.resolve();
    (window as unknown as { jasonDesktop: unknown }).jasonDesktop = {
        platform: 'win32',
        onCommand: quiet,
        theme: { get: () => Promise.resolve(null), set: done },
        links: { take: () => Promise.resolve(null), onOpen: quiet },
        chat: {
            send: done,
            stop: done,
            answer: done,
            onEvent: quiet,
            list: () => Promise.resolve([]),
            open: done,
            create: done,
            remove: done,
            restart: done,
            attach: done,
            rename: done,
            feed: done,
        },
        setup: {
            status: () =>
                Promise.resolve({
                    hosts: [],
                    reply: {
                        cli: { found: false, version: null, install: null },
                        signedIn: false,
                        user: null,
                        wired: false,
                        accounts: [],
                    },
                    runtimeVersion: null,
                    bundleVersion: '0.0.0-smoke',
                    versionsDiffer: null,
                    plugins: [],
                    settingsRefused: null,
                }),
            connectReply: done,
            connectReplyWithKey: done,
            setProviderKey: done,
            installProgram: done,
            configurePlugin: done,
            onChanged: quiet,
        },
    };
}

test('in the console a screen opened in the Page tab fills the panel, whichever screen it is', async ({
    page,
    jason,
}) => {
    await page.setViewportSize(WIDE);
    await page.addInitScript(quietDesktop);
    const { url } = await jason.uiOpen();
    await page.goto(url);
    await expect(page.locator('.dv-dockview')).toBeVisible();

    const pages = page.locator('.side nav[aria-label="Pages"] button');
    await expect(pages.first()).toBeVisible();
    const count = await pages.count();
    expect(count).toBeGreaterThanOrEqual(6);
    for (let at = 0; at < count; at += 1) {
        const name = (await pages.nth(at).innerText()).trim();
        await pages.nth(at).click();
        const content = page.locator('.harness .app.chromeless > .content');
        await expect(content, `${name} is shown in the Page tab`).toBeVisible();
        const panel = await page.locator('.harness').boundingBox();
        const drawn = await content.boundingBox();
        expect(panel, name).not.toBeNull();
        expect(drawn, name).not.toBeNull();
        expect(panel!.width, `${name}: the panel is wide`).toBeGreaterThan(600);
        expect(drawn!.width / panel!.width, `${name}: share of the panel's width it fills`).toBeGreaterThanOrEqual(
            FILLS,
        );
    }
});

test('in the browser shell the sidebar keeps its width and the content takes the rest', async ({ page, jason }) => {
    await page.setViewportSize(WIDE);
    const { url } = await jason.uiOpen();
    await page.goto(url);
    await expect(page.getByRole('navigation')).toBeVisible();

    const sidebar = await page.locator('.app > .sidebar').boundingBox();
    const content = await page.locator('.app > .content').boundingBox();
    expect(sidebar).not.toBeNull();
    expect(content).not.toBeNull();
    expect(sidebar!.width).toBe(220);
    // The content starts where the sidebar ends and runs to the window's edge.
    expect(content!.x).toBeCloseTo(sidebar!.x + sidebar!.width, 0);
    expect(content!.width).toBeCloseTo(WIDE.width - 220, 0);
});
