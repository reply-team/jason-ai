import { expect, test } from './fixtures';

// What the screen tests cannot see is where a screen is drawn. These rows measure the boxes in a real browser at a wide
// window: a screen shown in the console's one area fills it, the panels once drawn for a narrow column keep a measure of
// their own, Needs you is drawn across the whole area, and the console in a browser keeps its sidebar and gives the rest of
// the width to its area.
const WIDE = { width: 2000, height: 900 };
// A screen is drawn across the panel, not in a column of it: at least this share of the panel's width.
const FILLS = 0.9;
// The measure the panels drawn for a narrow column keep, and the words read in Needs you's pane with them.
const MEASURE = 880;

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

test('in the console a screen shown in the one area fills it, whichever screen it is', async ({ page, jason }) => {
    await page.setViewportSize(WIDE);
    await page.addInitScript(quietDesktop);
    const { url } = await jason.uiOpen();
    await page.goto(url);
    await expect(page.locator('.jc-console .area')).toBeVisible();

    // The screens a menu command opens in the area, as the console opens them: not through the sidebar, whose groups
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
        await expect(content, `${command} is shown in the area`).toBeVisible();
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

test('in the console Agents and Activity keep a measure in the wide area, centred in it', async ({ page, jason }) => {
    await page.setViewportSize(WIDE);
    await page.addInitScript(quietDesktop);
    const { url } = await jason.uiOpen();
    await page.goto(url);
    await expect(page.locator('.jc-console .area')).toBeVisible();
    await page.waitForFunction(
        () => typeof (window as unknown as { menuCommand?: unknown }).menuCommand === 'function',
    );
    for (const [command, title] of [
        ['navigate.campaigns', 'Agents'],
        ['navigate.activity', 'Activity'],
    ] as const) {
        await page.evaluate(
            (name) => (window as unknown as { menuCommand: (name: string) => void }).menuCommand(name),
            command,
        );
        // Resolved from the stable `.painted` element: a repaint replaces everything inside it, so a `.panel-body`
        // resolved before one is detached by the time it is measured. The screen's loading words are waited out, and the
        // panel is found and measured in one evaluate, which no repaint can interrupt.
        const painted = page.locator('.jc-console .area > .pane:not([hidden]) .painted');
        await expect(painted.getByRole('heading', { name: title, level: 1 }), command).toBeVisible();
        await expect(painted.getByText('Reading what waits…'), `${command}: still loading`).toHaveCount(0);
        // Measured against the panel it is drawn in, inside its scroll bar, so a panel long enough to scroll is
        // measured as a short one is.
        const room = await painted.evaluate((panel) => {
            const element = panel.querySelector<HTMLElement>('.panel-body');
            if (element === null) {
                return null;
            }
            const drawn = element.getBoundingClientRect();
            const inner = panel.getBoundingClientRect().left + panel.clientLeft;
            return {
                width: drawn.width,
                panelWidth: panel.clientWidth,
                left: drawn.left - inner,
                right: inner + panel.clientWidth - drawn.right,
            };
        });
        expect(room, `${command}: a panel body is drawn`).not.toBeNull();
        // Narrower than the panel, by a measure of its own, with as much room on its left as on its right.
        expect(room!.width, `${command}: its measure ${JSON.stringify(room)}`).toBeLessThanOrEqual(MEASURE);
        expect(room!.width, `${command}: narrower than the panel ${JSON.stringify(room)}`).toBeLessThan(
            room!.panelWidth,
        );
        expect(Math.abs(room!.left - room!.right), `${command}: centred ${JSON.stringify(room)}`).toBeLessThanOrEqual(
            2,
        );
    }
});

test('in the console Needs you is drawn across the whole wide area, as its two panes need', async ({ page, jason }) => {
    await page.setViewportSize(WIDE);
    await page.addInitScript(quietDesktop);
    const { url } = await jason.uiOpen();
    await page.goto(url);
    await expect(page.locator('.jc-console .area')).toBeVisible();
    await page.waitForFunction(
        () => typeof (window as unknown as { menuCommand?: unknown }).menuCommand === 'function',
    );
    await page.evaluate(() =>
        (window as unknown as { menuCommand: (name: string) => void }).menuCommand('navigate.attention'),
    );
    // What the runtime gives it: nothing waits in a fresh installation, and the panel is measured as it is drawn then.
    // The panes' columns and the measure of the words read in the pane are the sheet's, held by its row in the screen
    // layer, where there is something to read.
    const painted = page.locator('.jc-console .area > .pane:not([hidden]) .painted');
    await expect(painted.getByRole('heading', { name: 'Needs you', level: 1 })).toBeVisible();
    await expect(painted.getByText('Reading what waits…')).toHaveCount(0);
    const room = await painted.evaluate((panel) => {
        const drawn = panel.querySelector('.needs-panel')!.getBoundingClientRect();
        return { width: drawn.width, panelWidth: panel.clientWidth };
    });
    expect(room.panelWidth, `the panel is wide ${JSON.stringify(room)}`).toBeGreaterThan(MEASURE);
    expect(room.width / room.panelWidth, `Needs you fills the panel ${JSON.stringify(room)}`).toBeGreaterThanOrEqual(
        FILLS,
    );
});

test('in a browser tab with no desktop the sidebar keeps its width and the area takes the rest', async ({
    page,
    jason,
}) => {
    await page.setViewportSize(WIDE);
    const { url } = await jason.uiOpen();
    await page.goto(url);
    await expect(page.locator('.jc-console .area')).toBeVisible();

    const sidebar = await page.locator('.jc-console .side').boundingBox();
    const area = await page.locator('.jc-console .area').boundingBox();
    expect(sidebar).not.toBeNull();
    expect(area).not.toBeNull();
    expect(sidebar!.width).toBe(284);
    // The area starts where the sidebar ends and takes most of the rest of the window.
    expect(area!.x).toBeGreaterThanOrEqual(sidebar!.x + sidebar!.width - 1);
    expect(area!.width).toBeGreaterThan((WIDE.width - 284) * 0.8);
});
