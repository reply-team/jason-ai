import { expect, test } from './fixtures';

// The console is what the page mounts inside Jason Desktop. The desktop is stood in for by a script that runs before the
// page's own: the seam's shape and nothing behind it, so this proves the console's seams in a real browser, under the
// runtime's own policy, and leaves the chat's flows to the screen tests.
function standInDesktop(): void {
    const quiet = () => () => undefined;
    const done = () => Promise.resolve();
    (window as unknown as { jasonDesktop: unknown }).jasonDesktop = {
        platform: 'win32',
        onCommand: quiet,
        chat: { send: done, stop: done, answer: done, onEvent: quiet },
        setup: {
            status: () =>
                Promise.resolve({
                    hosts: [
                        {
                            id: 'claude',
                            label: 'Claude Code',
                            kind: 'program',
                            found: true,
                            version: '2.1.300',
                            drivesChat: true,
                            install: null,
                            belowFloor: null,
                        },
                    ],
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

test('inside the desktop the console mounts under the runtime policy, and follows the journal from the loopback page', async ({
    page,
    jason,
}) => {
    const violations: string[] = [];
    page.on('console', (message) => {
        if (/Content Security Policy|Refused to/.test(message.text())) violations.push(message.text());
    });
    page.on('pageerror', (error) => violations.push(`pageerror: ${error.message}`));
    const streams: string[] = [];
    let received = 0;
    page.on('websocket', (socket) => {
        streams.push(socket.url());
        socket.on('framereceived', () => (received += 1));
    });
    await page.addInitScript(standInDesktop);

    const { url } = await jason.uiOpen();
    const first = await page.goto(url);
    expect(first?.headers()['content-security-policy']).toBe(
        "default-src 'self'; frame-ancestors 'none'; base-uri 'none'",
    );
    await expect(page.getByRole('heading', { name: 'What should Jason work on?' })).toBeVisible();
    await expect(page.locator('.dv-dockview')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Needs you', level: 1 })).toBeVisible();

    // The built page loads the console's stylesheets as its own chunk's: a style only console.css gives, and one only
    // the dock's stylesheet gives, are applied. The element alone would be there unstyled.
    await expect(page.locator('.jc-console .shell')).toHaveCSS('display', 'flex');
    await expect(page.locator('.jc-console .side')).toHaveCSS('width', '232px');
    await expect(page.locator('.dv-tabs-and-actions-container').first()).toHaveCSS('display', 'flex');

    const side = page.locator('.side');
    await side.getByRole('button', { name: 'Activity' }).click();
    await expect(page.getByText('Following the journal')).toBeVisible();
    expect(streams).toEqual([`ws://127.0.0.1:${jason.port}/v1/journal.stream`]);

    await jason.campaignCreate('Smoke campaign');
    await expect(page.locator('.ev.activity').filter({ hasText: 'Smoke campaign' })).toBeVisible();
    expect(received).toBeGreaterThan(0);
    await side.getByRole('button', { name: 'Connect' }).click();
    await expect(page.getByText('Drives the chat')).toBeVisible();

    expect(violations).toEqual([]);
});
