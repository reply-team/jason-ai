import { expect, test } from './fixtures';

// The console is what the page mounts inside Jason Desktop. The desktop is stood in for by a script that runs before the
// page's own: the seam's shape and nothing behind it, so this proves the console's seams in a real browser, under the
// runtime's own policy, and leaves the chat's flows to the screen tests.
function standInDesktop(): void {
    const quiet = () => () => undefined;
    const done = () => Promise.resolve();
    // The conversations as the desktop answers for them: made by create, listed once sent to, and a refusal answered
    // as a value with the desktop's sentence, never a rejected promise.
    type Made = { id: string; campaignId: string | null; title: string; updatedAt: string; running: boolean };
    const made = new Map<string, Made>();
    const sent = new Set<string>();
    let count = 0;
    const notKept = { refused: 'That conversation is not one this desktop keeps.' };
    const kept = (id: unknown) => (typeof id === 'string' ? made.get(id) : undefined);
    // The Feed as the desktop answers for it, and every line the page handed it, for the test to read.
    const feed: Made = {
        id: 'conv_01JDFEED000000000000000000',
        campaignId: null,
        title: 'Feed',
        updatedAt: '2026-11-11T00:00:00.000Z',
        running: false,
    };
    const handed: unknown[] = [];
    (window as unknown as { handedToFeed: unknown[] }).handedToFeed = handed;
    (window as unknown as { jasonDesktop: unknown }).jasonDesktop = {
        platform: 'win32',
        onCommand: quiet,
        chat: {
            send: (id: unknown) => Promise.resolve(kept(id) ? (sent.add(id as string), null) : notKept),
            stop: done,
            answer: done,
            onEvent: quiet,
            list: () => Promise.resolve([...made.values()].filter((each) => sent.has(each.id))),
            open: (id: unknown) => {
                if (id === feed.id)
                    return Promise.resolve({ ...feed, main: true, entries: [], dropped: 0, chosen: null });
                const found = kept(id);
                return Promise.resolve(found ? { ...found, entries: [], dropped: 0 } : notKept);
            },
            create: (campaignId?: string) => {
                const found = [...made.values()].find(
                    (each) => campaignId !== undefined && each.campaignId === campaignId,
                );
                if (found) return Promise.resolve(found);
                const one: Made = {
                    id: `conv_01JD${String(++count).padStart(22, '0')}`,
                    campaignId: campaignId ?? null,
                    title: campaignId ?? 'New chat',
                    updatedAt: '2026-11-11T00:00:00.000Z',
                    running: false,
                };
                made.set(one.id, one);
                return Promise.resolve(one);
            },
            remove: (id: unknown) => Promise.resolve(kept(id) && made.delete(id as string) ? null : notKept),
            restart: (id: unknown) => Promise.resolve(kept(id) ? null : notKept),
            attach: () =>
                Promise.resolve({ refused: 'That campaign is not one this chat made. Open it from the campaign.' }),
            // As the desktop renames: refused before the first send, one line, cut at sixty characters.
            rename: (id: unknown, title: unknown) => {
                const found = kept(id);
                if (!found || !sent.has(found.id))
                    return Promise.resolve(
                        found ? { refused: 'Send this chat a message first; then it can be renamed.' } : notKept,
                    );
                const name = Array.from(typeof title === 'string' ? title.replace(/\s+/g, ' ').trim() : '')
                    .slice(0, 60)
                    .join('');
                if (name === '') return Promise.resolve({ refused: 'A name needs at least one character that shows.' });
                found.title = name;
                return Promise.resolve(found);
            },
            feed: (lines: unknown[]) => {
                handed.push(...lines);
                return Promise.resolve({ ...feed, main: true });
            },
        },
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
                            models: [{ id: 'opus', label: 'Opus', efforts: ['low', 'medium', 'high', 'xhigh', 'max'] }],
                            typedModel: true,
                            modelsUnread: null,
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
    // The line of the campaign made is handed to the Feed as its ids, moment and kind alone, and the Feed stands first.
    const toFeed = await page.evaluate(() => (window as unknown as { handedToFeed: unknown[] }).handedToFeed);
    expect(toFeed).toContainEqual(expect.objectContaining({ kind: 'campaign_created' }));
    for (const line of toFeed)
        expect(Object.keys(line as object).sort()).toEqual(['campaign_id', 'id', 'kind', 'ts', 'work_item_id']);
    await side.getByRole('navigation', { name: 'Feed' }).getByRole('button', { name: 'Feed' }).click();
    await expect(page.getByRole('heading', { name: 'The Feed' })).toBeVisible();
    await side.getByRole('button', { name: 'Connect' }).click();
    await expect(page.getByText('Drives the chat')).toBeVisible();

    expect(violations).toEqual([]);
});
