import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { join } from 'node:path';
import type { Page, TestInfo } from '@playwright/test';
import { expect, test } from '../fixtures';
import type { Jason } from '../harness';

// The seal in the browser, tried from inside: a hostile page stands under sealed/ of a build of one's own, opened by
// itself, and every way out it can name aims at a witness, a server of this test's own on the loopback that answers
// anything. Only the sealed set's content policy stands between the page's network and an address that answers, so the
// witness hearing nothing is the policy at work, and so is the page reaching no file of the runtime's own. The page's
// own word decides only what stays inside it: its origin and its storage. The probe runs in the pull request's CI alone,
// in each engine its workflow installs (playwright.config.ts).

const probe = join(import.meta.dirname, 'sealed-probe.html');

/** What the page itself decides: each must come back refused. */
const inside = [
    'origin',
    'localStorage',
    'sessionStorage',
    'cookie',
    'parent.document',
    'parent.localStorage',
    'parent.location',
    'indexedDB',
    'witness fetch',
    'witness fetch no-cors',
    'witness image',
    'witness websocket',
    'own fetch no-cors',
    'own image',
];
/** What the witness and this test's listeners decide: the page says only that it made them. */
const outside = ['witness window.open', 'alert'];

/**
 * Starts the tries that would move the page away (a form, a download, the top's address, the frame's own move), once
 * what the page wrote is read: a browser may begin to move a page before it refuses, and a page that is moving cannot be
 * read. The call is not awaited past a bound, as the page may be moving when it returns.
 */
async function leave(target: { evaluate: Page['evaluate'] }): Promise<void> {
    await Promise.race([
        target.evaluate(() => (window as unknown as { probeLeave: () => void }).probeLeave()).catch(() => undefined),
        new Promise((resolve) => setTimeout(resolve, 2_000)),
    ]);
    await new Promise((resolve) => setTimeout(resolve, 3_000));
}

/** A server that answers every request and every upgrade, and keeps what it heard. */
async function startWitness(): Promise<{ origin: string; heard: string[]; close: () => Promise<void> }> {
    const heard: string[] = [];
    const server = createServer((request, response) => {
        heard.push(`${request.method} ${request.url}`);
        response.writeHead(200, { 'Content-Type': 'text/plain', 'Access-Control-Allow-Origin': '*' });
        response.end('heard');
    });
    server.on('upgrade', (request, socket) => {
        heard.push(`UPGRADE ${request.url}`);
        socket.destroy();
    });
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
    const address = server.address();
    const port = typeof address === 'object' && address ? address.port : 0;
    return {
        origin: `http://127.0.0.1:${port}`,
        heard,
        close: () => new Promise((resolve) => server.close(() => resolve())),
    };
}

/** Where the probe stands: under sealed/ of a custom build, served over the default. */
const probePath = '/ui/sealed/probe.html';

/** The probe placed under the custom build's sealed directory, aiming at the witness. */
function placeProbe(jason: Jason, witness: string): void {
    jason.placeBuild('custom', 'probe');
    const page = readFileSync(probe, 'utf8').replaceAll('__WITNESS_ORIGIN__', witness);
    const sealed = join(jason.dataDir, 'ui', 'custom', 'sealed');
    mkdirSync(sealed, { recursive: true });
    writeFileSync(join(sealed, 'probe.html'), page);
}

/** What the probe wrote: each try with its outcome and its own words. */
async function outcomes(page: Page): Promise<[string, string, string][]> {
    const root = page.locator('body');
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

/** Every request and WebSocket the page makes, or a page it opened. */
function watchNetwork(page: Page) {
    const seen = { asked: [] as string[], finished: [] as string[], sockets: [] as string[], received: [] as string[] };
    page.on('request', (request) => seen.asked.push(request.url()));
    page.on('requestfinished', (request) => seen.finished.push(request.url()));
    page.on('websocket', (socket) => {
        seen.sockets.push(socket.url());
        socket.on('framereceived', () => seen.received.push(socket.url()));
    });
    return seen;
}

function record(info: TestInfo, seen: ReturnType<typeof watchNetwork>, heard: string[]): void {
    info.annotations.push({
        type: 'network',
        description: `requests ${seen.asked.length}, finished ${seen.finished.length}, websockets ${seen.sockets.length}, witness heard ${heard.length}`,
    });
}

test('a page under sealed/, opened by itself, has an origin of its own and reaches no address', async ({
    page,
    jason,
}, info) => {
    const witness = await startWitness();
    try {
        placeProbe(jason, witness.origin);
        const popups: string[] = [];
        const downloads: string[] = [];
        page.on('popup', (popup) => popups.push(popup.url()));
        page.on('download', (download) => downloads.push(download.suggestedFilename()));
        page.on('dialog', (dialog) => void dialog.dismiss());
        const seen = watchNetwork(page);
        await page.goto(`${jason.origin}${probePath}`);

        const written = await outcomes(page);
        expect(written.map(([name]) => name).sort()).toEqual([...inside, ...outside].sort());
        // Opened by itself the page is its own parent and top, so the tries of the page above are no tries here.
        const own = written.filter(([name]) => inside.includes(name) && !/^parent\./.test(name));
        expect(own.map(([name]) => name)).toContain('origin');
        expect(own.filter(([, outcome]) => outcome !== 'refused')).toEqual([]);

        await leave(page);
        record(info, seen, witness.heard);
        expect(witness.heard).toEqual([]);
        // A tab's icon at the root of the address is the browser's own request, made for any page, not the page's.
        const browsers = `${jason.origin}/favicon.ico`;
        expect(seen.finished.filter((each) => !each.startsWith('data:') && each !== browsers)).toEqual([
            `${jason.origin}${probePath}`,
        ]);
        expect(seen.received).toEqual([]);
        expect(popups).toEqual([]);
        expect(downloads).toEqual([]);
        expect(page.url()).toBe(`${jason.origin}${probePath}`);
    } finally {
        await witness.close();
    }
});
