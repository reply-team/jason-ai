import { expect, test } from './fixtures';

// What the screen tests cannot see is where a screen is drawn. These rows measure the boxes in a real browser at a wide
// window: a screen in the console's Page tab fills the panel, and the console in a browser keeps its sidebar and gives the
// rest of the width to its dock.
const WIDE = { width: 2000, height: 900 };
// A screen is drawn across the panel, not in a column of it: at least this share of the panel's width.
const FILLS = 0.9;

// The desktop's seam as far as the console needs it to mount and show a page: nothing answers, nothing is running.
function quietDesktop(): void {
    const quiet = () => () => undefined;
    const done = () => Promise.resolve();
    (window as unknown as { jasonDesktop: unknown }).jasonDesktop = {
        platform: 'win32',
        // The desktop's menu commands reach the console through this handler; the test sends them from the page.
        onCommand: (handler: unknown) => {
            (window as unknown as { menuCommand: unknown }).menuCommand = handler;
            return () => undefined;
        },
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

    // The screens a menu command opens in the Page tab, as the console opens them: not through the sidebar, whose groups
    // are not what is under test. The list must not be empty, so that the row cannot pass with nothing in it.
    const commands = ['performance', 'workitems', 'people', 'system', 'settings'].map((id) => `navigate.${id}`);
    expect(commands.length).toBeGreaterThanOrEqual(5);
    await page.waitForFunction(
        () => typeof (window as unknown as { menuCommand?: unknown }).menuCommand === 'function',
    );
    for (const command of commands) {
        await page.evaluate(
            (name) => (window as unknown as { menuCommand: (name: string) => void }).menuCommand(name),
            command,
        );
        const content = page.locator('.harness .app.chromeless > .content');
        await expect(content, `${command} is shown in the Page tab`).toBeVisible();
        // Wait for the screen's own words in the panel, so the box measured is the one of this screen.
        await expect(content.locator('h1').first(), command).toBeVisible();
        const panel = await page.locator('.harness').boundingBox();
        const drawn = await content.boundingBox();
        expect(panel, command).not.toBeNull();
        expect(drawn, command).not.toBeNull();
        expect(panel!.width, `${command}: the panel is wide`).toBeGreaterThan(600);
        expect(drawn!.width / panel!.width, `${command}: share of the panel's width it fills`).toBeGreaterThanOrEqual(
            FILLS,
        );
    }
});

test('in a browser tab with no desktop the sidebar keeps its width and the dock takes the rest', async ({
    page,
    jason,
}) => {
    await page.setViewportSize(WIDE);
    const { url } = await jason.uiOpen();
    await page.goto(url);
    await expect(page.locator('.dv-dockview')).toBeVisible();

    const sidebar = await page.locator('.jc-console .side').boundingBox();
    const dock = await page.locator('.dv-dockview').first().boundingBox();
    expect(sidebar).not.toBeNull();
    expect(dock).not.toBeNull();
    expect(sidebar!.width).toBe(232);
    // The dock starts where the sidebar ends and takes most of the rest of the window.
    expect(dock!.x).toBeGreaterThanOrEqual(sidebar!.x + sidebar!.width - 1);
    expect(dock!.width).toBeGreaterThan((WIDE.width - 232) * 0.8);
});
