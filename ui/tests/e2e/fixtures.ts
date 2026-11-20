import { test as base, expect, type Page } from '@playwright/test';
import { Jason } from './harness';

/** A runtime with the default build; a test that needs another shape starts its own with Jason.start and stops it. */
export const test = base.extend<{ jason: Jason }>({
    // eslint-disable-next-line no-empty-pattern
    jason: async ({}, use) => {
        const jason = await Jason.start();
        await use(jason);
        await jason.stop();
        jason.dispose();
    },
});

export { expect };

/**
 * Opens a page Live's sidebar draws no row for through Search, as a person does: the sidebar's own Search row (found by
 * its action, since Recent's "Search agents" button shares the name's prefix), the query, Enter on the first result.
 */
export async function openBySearch(page: Page, text: string): Promise<void> {
    await page.locator('.jc-console .side [data-action="side.search"]').click();
    await page.getByPlaceholder('Search agents, people, pages and settings').fill(text);
    await page.keyboard.press('Enter');
}

/** Opens the System screen in the console's area, through Search. */
export async function openSystem(page: Page): Promise<void> {
    await openBySearch(page, 'System');
}

/**
 * The desktop's seam as far as the console needs it to mount and show a page: nothing answers, nothing is running. A plain
 * tab draws one message and not the console, so a test of the console stands this in, as an init script, first.
 */
export function quietDesktop(): void {
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
            open: (id: unknown) =>
                Promise.resolve({
                    id,
                    campaignId: null,
                    title: 'Feed',
                    updatedAt: '2026-11-11T00:00:00.000Z',
                    running: false,
                    main: true,
                    entries: [],
                    dropped: 0,
                    chosen: null,
                }),
            // A conversation as the desktop makes one: the console reads its id as soon as it asks for one.
            create: () =>
                Promise.resolve({
                    id: 'conv_01JD0000000000000000000001',
                    campaignId: null,
                    title: 'New chat',
                    updatedAt: '2026-11-11T00:00:00.000Z',
                    running: false,
                }),
            remove: done,
            restart: done,
            attach: done,
            rename: done,
            feed: () =>
                Promise.resolve({
                    id: 'conv_01JD0000000000000000000002',
                    campaignId: null,
                    title: 'Feed',
                    updatedAt: '2026-11-11T00:00:00.000Z',
                    running: false,
                    main: true,
                }),
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
            useProgram: done,
            configurePlugin: done,
            onChanged: quiet,
        },
    };
}
