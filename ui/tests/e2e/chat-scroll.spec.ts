import { expect, test } from './fixtures';

// The screen tests lay a chat out by a rule, since their page has no layout, so they pass whether or not a real window
// moves. This row has a real engine lay the console out: an answer taller than the window streams into the chat after a
// send from its foot, and the foot stays in view; the person scrolls up, more comes, and their place is kept; back at the
// foot, the thread follows again. The desktop is stood in for by a script that runs before the page's own: it makes and
// keeps a conversation as the desktop does, and hands the test the chat's event handler, through which the AI's words are
// streamed as the desktop streams them.
function talkingDesktop(): void {
    const quiet = () => () => undefined;
    const done = () => Promise.resolve();
    type Made = { id: string; campaignId: string | null; title: string; updatedAt: string; running: boolean };
    const made = new Map<string, Made>();
    const sentTo: string[] = [];
    const page = window as unknown as { sentTo: string[]; chatEvent?: (id: string, event: unknown) => void };
    page.sentTo = sentTo;
    let count = 0;
    const notKept = { refused: 'That conversation is not one this desktop keeps.' };
    const feed = {
        id: 'conv_01JDFEED000000000000000000',
        campaignId: null,
        title: 'Feed',
        updatedAt: '2026-11-11T00:00:00.000Z',
        running: false,
        main: true,
    };
    (window as unknown as { jasonDesktop: unknown }).jasonDesktop = {
        platform: 'win32',
        onCommand: quiet,
        theme: { get: () => Promise.resolve(null), set: done },
        links: { take: () => Promise.resolve(null), onOpen: quiet },
        chat: {
            send: (id: unknown) => {
                if (typeof id !== 'string' || !made.has(id)) return Promise.resolve(notKept);
                sentTo.push(id);
                return Promise.resolve(null);
            },
            stop: done,
            answer: done,
            onEvent: (handler: (id: string, event: unknown) => void) => {
                page.chatEvent = handler;
                return () => undefined;
            },
            list: () => Promise.resolve([...made.values()].filter((each) => sentTo.includes(each.id))),
            open: (id: unknown) => {
                if (id === feed.id) return Promise.resolve({ ...feed, entries: [], dropped: 0, chosen: null });
                const found = typeof id === 'string' ? made.get(id) : undefined;
                return Promise.resolve(found ? { ...found, entries: [], dropped: 0 } : notKept);
            },
            create: (campaignId?: string) => {
                const one: Made = {
                    id: `conv_01JD${String(++count).padStart(22, '0')}`,
                    campaignId: campaignId ?? null,
                    title: 'New chat',
                    updatedAt: '2026-11-11T00:00:00.000Z',
                    running: false,
                };
                made.set(one.id, one);
                return Promise.resolve(one);
            },
            remove: done,
            restart: done,
            attach: done,
            rename: done,
            feed: () => Promise.resolve(feed),
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
                            ownServers: [],
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
            useProgram: done,
            configurePlugin: done,
            onChanged: quiet,
        },
    };
}

test('the chat follows an answer taller than the window from its foot, keeps the place of a person who scrolled up, and follows again at the foot', async ({
    page,
    jason,
}) => {
    await page.setViewportSize({ width: 1280, height: 720 });
    await page.addInitScript(talkingDesktop);
    const { url } = await jason.uiOpen();
    await page.goto(url);

    // The first message, from Start: the conversation is made at that send and shown, its composer at the foot.
    const start = page.getByRole('form', { name: 'Start' });
    await start.getByRole('textbox', { name: 'Who do you want to reach?' }).fill('Find me ten founders');
    await start.getByRole('button', { name: 'Send' }).click();
    await page.waitForFunction(() => (window as unknown as { sentTo: string[] }).sentTo.length === 1);
    const id = await page.evaluate(() => (window as unknown as { sentTo: string[] }).sentTo[0]!);
    const pane = page.locator('.jc-console .area > .pane:not([hidden])');
    const scroller = pane.locator('.chat-scroll');
    await expect(scroller.getByText('Find me ten founders')).toBeVisible();

    const say = (text: string) =>
        page.evaluate(
            ([chat, words]) =>
                (window as unknown as { chatEvent: (id: string, event: unknown) => void }).chatEvent(chat!, {
                    kind: 'text',
                    text: words,
                }),
            [id, text],
        );
    const part = (n: number) => `Part ${n}. ${'A line of the answer that runs on and on. '.repeat(30)}\n\n`;
    // Two frames: what was added has been laid out and every scroll it set off has been dispatched.
    const frames = () =>
        page.evaluate(() => new Promise((done) => requestAnimationFrame(() => requestAnimationFrame(done))));
    const place = () =>
        scroller.evaluate((box) => ({
            top: box.scrollTop,
            fromFoot: box.scrollHeight - box.scrollTop - box.clientHeight,
            height: box.scrollHeight,
            view: box.clientHeight,
        }));

    // The answer comes in parts, at the foot: each is followed.
    for (const n of [1, 2, 3, 4]) {
        await say(part(n));
        await expect(scroller).toContainText(`Part ${n}.`);
        await frames();
        expect((await place()).fromFoot, `part ${n} is followed`).toBeLessThanOrEqual(1);
    }
    const followed = await place();
    expect(followed.height, 'the answer is taller than the view').toBeGreaterThan(followed.view * 2);
    expect(followed.top, 'the thread moved').toBeGreaterThan(0);
    await expect(page.getByText('Thinking…'), 'the foot of the thread is in view').toBeInViewport({ ratio: 1 });
    // The box that moved is the one that overflows: the pane and the panel around it never scroll.
    for (const outer of [pane, pane.locator('.chat-panel')])
        expect(await outer.evaluate((each) => each.scrollHeight - each.clientHeight)).toBeLessThanOrEqual(0);

    // The person scrolls up with the wheel, well past the measure; what comes then leaves their place alone.
    await scroller.hover();
    await page.mouse.wheel(0, -600);
    await expect.poll(async () => (await place()).fromFoot).toBeGreaterThan(120);
    await frames();
    const kept = (await place()).top;
    await say(part(5));
    await expect(scroller).toContainText('Part 5.');
    await frames();
    expect((await place()).top, 'the place is kept').toBe(kept);
    expect((await place()).fromFoot).toBeGreaterThan(120);

    // Back at the foot, the thread follows again.
    await page.mouse.wheel(0, 100_000);
    await expect.poll(async () => (await place()).fromFoot).toBeLessThanOrEqual(1);
    await frames();
    await say(part(6));
    await expect(scroller).toContainText('Part 6.');
    await frames();
    expect((await place()).fromFoot, 'part 6 is followed').toBeLessThanOrEqual(1);
});
