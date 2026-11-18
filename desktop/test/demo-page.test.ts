// The Demo is a static page shown as it was built, so its view is held harmless whatever its script does: no preload,
// the sandbox, a session of its own in memory, the one file it is served from, no pop-up, no navigation away, no
// download, every permission and device refused, and the content policy as a header. Each piece runs here over a
// stand-in for Electron's contents and session that records what was installed and lets the test act as the page.

import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { runInNewContext } from 'node:vm';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import type { WebContents } from 'electron';
import {
    answerDemo,
    deadProxy,
    demoHeaders,
    demoPage,
    demoPolicy,
    demoSchemes,
    demoSwitch,
    demoViewPreferences,
    navigationAllowed,
    onceEach,
    requestAllowed,
    sealView,
    demoGeometry,
    demoLayout,
    demoMacSheet,
    demoPageSheet,
    demoPageBoundMs,
    demoPageShowing,
    demoStrip,
    demoStripAt,
    demoStripSheets,
    demoBeforeReady,
    demoBandSheet,
    demoSwitchMost,
    demoLinkGestureMs,
    demoLinkMark,
    demoLinkOpener,
    demoLinkScript,
    demoLinkSheet,
    demoLinkSpanMs,
    demoLinkWorld,
    demoRepository,
    demoRepositoryText,
    demoResolverRule,
    noDragSheet,
    switchChoice,
    isDemoLinkGesture,
    watchDemoGestures,
    type DemoRefusal,
} from '../src/demo-page.ts';
import { persists } from '../src/window-pairing.ts';
import { stripHeight, titleBand } from '../src/window-frame.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const pageBytes = new TextEncoder().encode('<!doctype html><title>page</title>');

// ---- The view's preferences, its session and its scheme ------------------------------------------------------------

test('a view of the demo has no preload, the sandbox, isolation, no Node anywhere, no webview, no DevTools, no drag navigation, no dialogs', () => {
    for (const view of [demoPage, demoSwitch]) {
        const preferences = demoViewPreferences(view);
        assert.ok(!('preload' in preferences), 'no preload, so no bridge');
        assert.deepEqual(preferences, {
            partition: view.partition,
            sandbox: true,
            contextIsolation: true,
            nodeIntegration: false,
            nodeIntegrationInWorker: false,
            nodeIntegrationInSubFrames: false,
            webviewTag: false,
            webSecurity: true,
            allowRunningInsecureContent: false,
            experimentalFeatures: false,
            plugins: false,
            enableWebSQL: false,
            navigateOnDragDrop: false,
            spellcheck: false,
            devTools: false,
            safeDialogs: true,
            disableDialogs: true,
            autoplayPolicy: 'document-user-activation-required',
        });
    }
});

test('each view has a session of its own, which Electron keeps in memory, apart from the window’s and from each other', () => {
    assert.equal(persists(demoPage.partition), false);
    assert.equal(persists(demoSwitch.partition), false);
    assert.notEqual(demoPage.partition, demoSwitch.partition);
    assert.notEqual(demoPage.partition, 'jason-window');
    assert.notEqual(demoPage.scheme, demoSwitch.scheme);
});

test('the two schemes are standard and secure and nothing more: the policy is never bypassed, no fetch, CORS, service worker or stream', () => {
    assert.deepEqual(demoSchemes(), [
        { scheme: 'jason-demo', privileges: { standard: true, secure: true } },
        { scheme: 'jason-switch', privileges: { standard: true, secure: true } },
    ]);
});

test('the content policy allows the page’s inline script and style and its data: fonts and images, and nothing else', () => {
    assert.equal(
        demoPolicy,
        "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; font-src data:; img-src data:; connect-src 'none'; media-src 'none'; object-src 'none'; frame-src 'none'; child-src 'none'; worker-src 'none'; manifest-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors 'none'; sandbox allow-scripts",
    );
    // The page's own frame: scripts, and nothing more. No same origin, so its origin is opaque and it sets no cookie;
    // what it stores stays in the view's in-memory session. No forms.
    assert.ok(!/allow-same-origin|allow-forms/.test(demoPolicy));
    // No source names an address, a scheme to fetch from, 'self' or eval.
    assert.ok(!/https?:|wss?:|blob:|'self'|unsafe-eval|\*/.test(demoPolicy));
    // The sandbox leaves out pop-ups, top navigation, downloads, modals, printing and the rest.
    assert.ok(!/allow-(popups|top-navigation|downloads|modals|pointer-lock|presentation|orientation-lock|storage-access)/.test(demoPolicy));
});

test('the one answer carries the policy as a header, denies every feature, and is never cached', () => {
    const headers = demoHeaders();
    assert.equal(headers['Content-Security-Policy'], demoPolicy);
    assert.equal(headers['Content-Type'], 'text/html; charset=utf-8');
    assert.equal(headers['X-Content-Type-Options'], 'nosniff');
    assert.equal(headers['X-DNS-Prefetch-Control'], 'off');
    assert.equal(headers['Referrer-Policy'], 'no-referrer');
    assert.equal(headers['Cache-Control'], 'no-store');
    for (const feature of ['camera', 'microphone', 'geolocation', 'display-capture', 'fullscreen', 'clipboard-read', 'clipboard-write', 'usb', 'hid', 'serial', 'bluetooth', 'payment']) {
        assert.ok(headers['Permissions-Policy']?.split(', ').includes(`${feature}=()`), feature);
    }
});

test('the scheme serves the one address to GET alone, at any fragment; another path, a query or a method gets nothing, and the file is not read', () => {
    let reads = 0;
    const read = () => {
        reads++;
        return pageBytes;
    };
    const served = answerDemo(demoPage, demoPage.url, 'GET', read);
    assert.equal(served.status, 200);
    assert.deepEqual(served.headers, demoHeaders());
    assert.equal(served.body, pageBytes);
    assert.equal(reads, 1);
    for (const [url, method] of [
        ['jason-demo://page/other.html', 'GET'],
        ['jason-demo://page/index.html?x=1', 'GET'],
        ['jason-demo://page/other.html#start', 'GET'],
        ['jason-demo://page/index.html?x=1#start', 'GET'],
        ['jason-demo://page/', 'GET'],
        ['jason-demo://other/index.html', 'GET'],
        ['jason-demo://page/../index.html', 'GET'],
        [demoPage.url, 'POST'],
        [demoSwitch.url, 'GET'],
    ] as const) {
        const answer = answerDemo(demoPage, url, method, read);
        assert.equal(answer.status, 404, `${method} ${url}`);
        assert.equal(answer.body, null);
    }
    assert.equal(reads, 1, 'nothing else was read');
});

test('a request goes on only to the view’s one address or as a data: address; the world, the loopback and the disk are refused', () => {
    assert.ok(requestAllowed(demoPage, demoPage.url));
    assert.ok(requestAllowed(demoPage, 'data:font/woff2;base64,AAAA'));
    for (const url of [
        'https://example.com/x',
        'http://127.0.0.1:5000/v1/campaign.list',
        'ws://127.0.0.1:5000/v1/journal.stream',
        'wss://example.com/',
        'file:///C:/Users/someone/.jason/config/settings.json',
        'file:///etc/passwd',
        'jason-demo://page/other.html',
        'jason-switch://switch/index.html',
        'jason-desktop://feed',
        'blob:jason-demo://page/0000',
        'chrome://gpu',
        'devtools://devtools/bundled/inspector.html',
        'about:blank',
    ]) {
        assert.ok(!requestAllowed(demoPage, url), url);
    }
});

test('a navigation goes on only as a reload of the page in the main frame; anywhere else, and any frame, is refused', () => {
    assert.ok(navigationAllowed(demoPage, demoPage.url, true));
    assert.ok(!navigationAllowed(demoPage, demoPage.url, false));
    for (const url of ['https://example.com/', 'http://127.0.0.1:5000/', 'file:///C:/', 'about:blank', 'about:srcdoc', 'data:text/html,x', 'javascript:alert(1)', 'mailto:a@example.com', 'jason-switch://switch/live', 'jason-demo://page/index.html?x']) {
        assert.ok(!navigationAllowed(demoPage, url, true), url);
    }
});

test('the person’s choice is read from our switch’s one link, Live; nothing else is a choice', () => {
    assert.equal(switchChoice('jason-switch://switch/live'), 'live');
    for (const url of ['jason-switch://switch/demo', 'jason-switch://switch/live?x', 'jason-switch://switch/live/', 'jason-demo://page/live', 'https://example.com/live', 'jason-switch://switch/LIVE']) {
        assert.equal(switchChoice(url), null, url);
    }
});

test('a refusal of each kind is told once, so a page asking again and again cannot fill the log', () => {
    const told: DemoRefusal[] = [];
    const refuse = onceEach((kind) => told.push(kind));
    for (let i = 0; i < 1000; i++) {
        refuse('navigation');
        refuse('request');
    }
    refuse('popup');
    assert.deepEqual(told, ['navigation', 'request', 'popup']);
});

// ---- The seal, wired onto a stand-in of Electron's contents and session ----------------------------------------------

type Handler = (...args: never[]) => unknown;

interface Stand {
    contents: WebContents;
    on: Map<string, Handler>;
    sessionOn: Map<string, Handler>;
    protocols: Map<string, Handler>;
    calls: string[];
    handler(name: string): Handler;
}

/** Contents and a session that record every handler installed on them; nothing of Electron runs. */
function stand(): Stand {
    const on = new Map<string, Handler>();
    const sessionOn = new Map<string, Handler>();
    const protocols = new Map<string, Handler>();
    const handlers = new Map<string, Handler>();
    const calls: string[] = [];
    const set = (name: string) => (handler: Handler) => {
        calls.push(name);
        handlers.set(name, handler);
    };
    const session = {
        protocol: { handle: (scheme: string, handler: Handler) => void protocols.set(scheme, handler) },
        webRequest: { onBeforeRequest: set('onBeforeRequest') },
        setPermissionRequestHandler: set('setPermissionRequestHandler'),
        setPermissionCheckHandler: set('setPermissionCheckHandler'),
        setDevicePermissionHandler: set('setDevicePermissionHandler'),
        setDisplayMediaRequestHandler: set('setDisplayMediaRequestHandler'),
        setBluetoothPairingHandler: set('setBluetoothPairingHandler'),
        on: (event: string, handler: Handler) => void sessionOn.set(event, handler),
        setSpellCheckerEnabled: (on: boolean) => void calls.push(`setSpellCheckerEnabled:${on}`),
        setProxy: async (config: unknown) => void calls.push(`setProxy:${JSON.stringify(config)}`),
    };
    const contents = {
        session,
        on: (event: string, handler: Handler) => void on.set(event, handler),
        setWindowOpenHandler: set('setWindowOpenHandler'),
        setWebRTCIPHandlingPolicy: (policy: string) => void calls.push(`setWebRTCIPHandlingPolicy:${policy}`),
        closeDevTools: () => void calls.push('closeDevTools'),
        insertCSS: async (css: string, options?: { cssOrigin?: string }) => {
            calls.push(`insertCSS:${options?.cssOrigin}:${css}`);
            return 'key';
        },
        executeJavaScriptInIsolatedWorld: async (world: number, scripts: { code: string }[]) => {
            calls.push(`executeJavaScriptInIsolatedWorld:${world}:${scripts.map((script) => script.code).join('')}`);
            return undefined;
        },
        loadURL: async (url: string) => void calls.push(`loadURL:${url}`),
        isDestroyed: () => false,
    };
    return {
        contents: contents as unknown as WebContents,
        on,
        sessionOn,
        protocols,
        calls,
        handler: (name) => {
            const found = handlers.get(name);
            assert.ok(found, `${name} was installed`);
            return found;
        },
    };
}

/** An event a handler may stop, recording whether it did. */
function event(fields: Record<string, unknown> = {}): { preventDefault(): void; stopped: boolean } & Record<string, unknown> {
    const made = { ...fields, stopped: false, preventDefault: () => void (made.stopped = true) };
    return made;
}

async function sealed(view = demoPage) {
    const made = stand();
    const refused: DemoRefusal[] = [];
    const asked: string[] = [];
    // As the window's reader does: only our switch's Live is a choice.
    const ask = (url: string) => {
        asked.push(url);
        return view === demoSwitch && switchChoice(url) !== null;
    };
    await sealView(made.contents, view, { read: () => pageBytes, refused: (kind) => refused.push(kind), asked: ask });
    return { ...made, refused, asked };
}

const call = (handler: Handler | undefined, ...args: unknown[]): unknown => {
    assert.ok(handler, 'the handler was installed');
    return (handler as (...args: unknown[]) => unknown)(...args);
};

test('every handler of the seal is installed on the contents and the session, the proxy last', async () => {
    const view = await sealed();
    assert.deepEqual([...view.on.keys()].sort(), ['certificate-error', 'devtools-opened', 'did-navigate', 'dom-ready', 'login', 'select-bluetooth-device', 'will-attach-webview', 'will-frame-navigate', 'will-navigate', 'will-prevent-unload', 'will-redirect']);
    assert.deepEqual([...view.sessionOn.keys()].sort(), ['select-hid-device', 'select-serial-port', 'select-usb-device', 'will-download']);
    assert.deepEqual([...view.protocols.keys()].sort(), ['file', 'http', 'https', 'jason-demo']);
    for (const name of ['onBeforeRequest', 'setPermissionRequestHandler', 'setPermissionCheckHandler', 'setDevicePermissionHandler', 'setDisplayMediaRequestHandler', 'setBluetoothPairingHandler', 'setWindowOpenHandler', 'setSpellCheckerEnabled:false', 'setWebRTCIPHandlingPolicy:disable_non_proxied_udp']) {
        assert.ok(view.calls.includes(name), name);
    }
    assert.equal(view.calls.at(-1), `setProxy:${JSON.stringify(deadProxy)}`);
    assert.deepEqual(deadProxy, { mode: 'fixed_servers', proxyRules: 'http://127.0.0.1:1', proxyBypassRules: '<-loopback>' });
});

test('the scheme answers the page with the policy as a header, and anything else with nothing; http, https and file answer nothing', async () => {
    const view = await sealed();
    const page = (await call(view.protocols.get('jason-demo'), { url: demoPage.url, method: 'GET' })) as Response;
    assert.equal(page.status, 200);
    assert.equal(page.headers.get('content-security-policy'), demoPolicy);
    assert.equal(await page.text(), new TextDecoder().decode(pageBytes));
    const other = (await call(view.protocols.get('jason-demo'), { url: 'jason-demo://page/secret', method: 'GET' })) as Response;
    assert.equal(other.status, 404);
    for (const scheme of ['http', 'https', 'file']) {
        const answer = (await call(view.protocols.get(scheme), { url: `${scheme}://x/`, method: 'GET' })) as Response;
        assert.equal(answer.status, 403, scheme);
        assert.equal(await answer.text(), '');
    }
    assert.ok(view.refused.includes('request'));
});

test('the session cancels every request but the page’s own, a WebSocket and a file included', async () => {
    const view = await sealed();
    const verdict = (url: string) => {
        let answer: { cancel?: boolean } | undefined;
        call(view.handler('onBeforeRequest'), { url }, (response: { cancel?: boolean }) => (answer = response));
        return answer?.cancel;
    };
    assert.equal(verdict(demoPage.url), false);
    for (const url of ['https://example.com/', 'ws://127.0.0.1:1/', 'file:///etc/passwd', 'http://127.0.0.1:5000/v1/campaign.list']) assert.equal(verdict(url), true, url);
});

test('every permission is refused, asked or checked, a device’s, a screen’s, a Bluetooth pairing’s and every chooser’s too', async () => {
    const view = await sealed();
    for (const permission of ['clipboard-sanitized-write', 'media', 'notifications', 'openExternal', 'fullscreen', 'pointerLock', 'geolocation', 'display-capture', 'midiSysex', 'unknown']) {
        let granted: boolean | undefined;
        call(view.handler('setPermissionRequestHandler'), view.contents, permission, (answer: boolean) => (granted = answer), {});
        assert.equal(granted, false, permission);
        assert.equal(call(view.handler('setPermissionCheckHandler'), view.contents, permission, demoPage.url, {}), false, permission);
    }
    assert.equal(call(view.handler('setDevicePermissionHandler'), { deviceType: 'usb' }), false);
    let streams: unknown = 'unanswered';
    call(view.handler('setDisplayMediaRequestHandler'), {}, (answer: unknown) => (streams = answer));
    assert.equal(streams, null);
    let pairing: unknown;
    call(view.handler('setBluetoothPairingHandler'), {}, (answer: unknown) => (pairing = answer));
    assert.deepEqual(pairing, { confirmed: false });
    for (const [name, picked] of [['select-hid-device', undefined], ['select-serial-port', ''], ['select-usb-device', undefined]] as const) {
        const stop = event();
        let chosen: unknown = 'unanswered';
        const callback = (answer?: unknown) => (chosen = answer);
        if (name === 'select-serial-port') call(view.sessionOn.get(name), stop, [{ portId: 'p' }], view.contents, callback);
        else call(view.sessionOn.get(name), stop, { deviceList: [{ deviceId: 'd' }] }, callback);
        assert.ok(stop.stopped, name);
        assert.equal(chosen, picked, name);
    }
    const bluetooth = event();
    let device: unknown = 'unanswered';
    call(view.on.get('select-bluetooth-device'), bluetooth, [{ deviceId: 'd' }], (answer: string) => (device = answer));
    assert.ok(bluetooth.stopped);
    assert.equal(device, '');
    assert.ok(view.refused.includes('permission') && view.refused.includes('device'));
});

test('no navigation leaves the page, in the main frame or any other, and a redirect is held the same way; a reload goes on', async () => {
    const view = await sealed();
    for (const name of ['will-navigate', 'will-frame-navigate', 'will-redirect']) {
        for (const [url, isMainFrame] of [['https://example.com/', true], ['file:///C:/Users/', true], ['about:srcdoc', false], [demoPage.url, false], ['mailto:a@example.com', true]] as const) {
            const stop = event({ url, isMainFrame });
            call(view.on.get(name), stop);
            assert.ok(stop.stopped, `${name} ${url}`);
        }
        const reload = event({ url: demoPage.url, isMainFrame: true });
        call(view.on.get(name), reload);
        assert.equal(reload.stopped, false, `${name} lets the reload go`);
    }
    assert.ok(view.refused.includes('navigation'));
    assert.ok(view.asked.includes('https://example.com/'), 'a refused navigation is told to the view’s reader, which acts only on our switch');
});

test('a pop-up is denied and opens nothing, the system browser included; a webview is refused', async () => {
    const view = await sealed();
    assert.deepEqual(call(view.handler('setWindowOpenHandler'), { url: 'https://example.com/' }), { action: 'deny' });
    const webview = event();
    call(view.on.get('will-attach-webview'), webview, {}, {});
    assert.ok(webview.stopped);
    assert.ok(view.refused.includes('popup') && view.refused.includes('webview'));
});

test('a download is cancelled before it starts', async () => {
    const view = await sealed();
    const stop = event();
    let cancelled = false;
    call(view.sessionOn.get('will-download'), stop, { cancel: () => void (cancelled = true) }, view.contents);
    assert.ok(stop.stopped);
    assert.ok(cancelled);
    assert.ok(view.refused.includes('download'));
});

test('the page cannot hold the window open or the quit with beforeunload, open DevTools, ask for a password or accept a bad certificate', async () => {
    const view = await sealed();
    const unload = event();
    call(view.on.get('will-prevent-unload'), unload);
    assert.ok(unload.stopped, 'the unload goes on');
    call(view.on.get('devtools-opened'));
    assert.ok(view.calls.includes('closeDevTools'));
    const login = event();
    let credentials: unknown[] | undefined;
    call(view.on.get('login'), login, {}, {}, (...given: unknown[]) => (credentials = given));
    assert.ok(login.stopped);
    assert.deepEqual(credentials, [], 'cancelled, no name and no password');
    const certificate = event();
    let trusted: boolean | undefined;
    call(view.on.get('certificate-error'), certificate, 'https://example.com/', 'net::ERR_CERT_INVALID', {}, (answer: boolean) => (trusted = answer));
    assert.equal(trusted, false);
});

test('a session already sealed in this process is not sealed again; the contents of a second view are', async () => {
    const made = stand();
    const refused: DemoRefusal[] = [];
    await sealView(made.contents, demoSwitch, { read: () => pageBytes, refused: (kind) => refused.push(kind) });
    const protocolsOnce = made.protocols.size;
    made.on.clear();
    await sealView(made.contents, demoSwitch, { read: () => pageBytes, refused: (kind) => refused.push(kind) });
    assert.equal(made.protocols.size, protocolsOnce);
    assert.equal(made.calls.filter((name) => name.startsWith('setProxy')).length, 1);
    assert.ok(made.on.has('will-navigate'), 'the second view’s contents are sealed');
});

test('our switch reads the person’s choice from the navigation its view refused', async () => {
    const view = await sealed(demoSwitch);
    const live = event({ url: 'jason-switch://switch/live', isMainFrame: true });
    call(view.on.get('will-navigate'), live);
    assert.ok(live.stopped, 'the view stays where it is');
    assert.deepEqual(view.asked, ['jason-switch://switch/live']);
    assert.equal(switchChoice(view.asked[0] ?? ''), 'live');
    // The person's own click is a choice, not a refusal: nothing is told to the log as one.
    assert.deepEqual(view.refused, []);
    const elsewhere = event({ url: 'https://example.com/', isMainFrame: true });
    call(view.on.get('will-navigate'), elsewhere);
    assert.ok(elsewhere.stopped);
    assert.deepEqual(view.refused, ['navigation'], 'any other navigation of the switch is still a refusal');
});

test('a reload of the page at any fragment draws the page: the navigation, the request, the answer and the commit all go on', async () => {
    // The page moves itself to `#start` once it is loaded, so the menu's Reload asks for its address with a fragment.
    for (const view of [demoPage, demoSwitch]) {
        for (const url of [`${view.url}#start`, `${view.url}#`, `${view.url}#a/b?c#d`]) {
            const sealedView = await sealed(view);
            for (const name of ['will-navigate', 'will-frame-navigate', 'will-redirect']) {
                const reload = event({ url, isMainFrame: true });
                call(sealedView.on.get(name), reload);
                assert.equal(reload.stopped, false, `${name} lets ${url} go`);
            }
            let cancelled: boolean | undefined;
            call(sealedView.handler('onBeforeRequest'), { url }, (answer: { cancel: boolean }) => void (cancelled = answer.cancel));
            assert.equal(cancelled, false, `the request for ${url} goes on`);
            const answer = (await call(sealedView.protocols.get(view.scheme), { url, method: 'GET' })) as Response;
            assert.equal(answer.status, 200, `the scheme serves ${url}`);
            assert.equal(await answer.text(), new TextDecoder().decode(pageBytes));
            call(sealedView.on.get('did-navigate'), event(), url, 200, 'OK');
            assert.ok(!sealedView.calls.some((name) => name.startsWith('loadURL:')), `${url} is the page's own address`);
            assert.deepEqual(sealedView.refused, [], `nothing refused at ${url}`);
            assert.deepEqual(sealedView.asked, []);
        }
    }
});

test('only the fragment is ignored: another scheme, host, path or query with the page’s fragment is refused everywhere', async () => {
    for (const url of [
        'jason-demo://page/other.html#start',
        'jason-demo://page/index.html?x=1#start',
        'jason-demo://other/index.html#start',
        'jason-demo://page/#start',
        'jason-demo://page/index.htm#start',
        'jason-demo://page/index.html/#start',
        'jason-switch://page/index.html#start',
        'https://page/index.html#start',
        `${demoSwitch.url}#start`,
        'about:blank#start',
    ]) {
        assert.ok(!requestAllowed(demoPage, url), `request ${url}`);
        assert.ok(!navigationAllowed(demoPage, url, true), `navigation ${url}`);
        assert.equal(answerDemo(demoPage, url, 'GET', () => pageBytes).status, 404, `answer ${url}`);
        const view = await sealed(demoPage);
        call(view.on.get('did-navigate'), event(), url, 200, 'OK');
        assert.deepEqual(view.calls.filter((name) => name.startsWith('loadURL:')), [`loadURL:${demoPage.url}`], `${url} is undone`);
        assert.deepEqual(view.refused, ['navigation']);
    }
    assert.ok(!navigationAllowed(demoPage, `${demoPage.url}#start`, false), 'a frame at the page’s address is still refused');
    assert.equal(answerDemo(demoPage, `${demoPage.url}#start`, 'POST', () => pageBytes).status, 404);
});

test('a navigation that ends anywhere but the view’s own address, as about:blank does with no will-navigate, loads the page again', async () => {
    for (const view of [demoPage, demoSwitch]) {
        const sealedView = await sealed(view);
        call(sealedView.on.get('did-navigate'), event(), view.url, 200, 'OK');
        assert.ok(!sealedView.calls.some((name) => name.startsWith('loadURL:')), 'its own address stays');
        assert.deepEqual(sealedView.refused, []);
        for (const url of ['about:blank', 'about:srcdoc', 'data:text/html,x']) call(sealedView.on.get('did-navigate'), event(), url, 200, 'OK');
        assert.deepEqual(
            sealedView.calls.filter((name) => name.startsWith('loadURL:')),
            [`loadURL:${view.url}`, `loadURL:${view.url}`, `loadURL:${view.url}`],
        );
        assert.deepEqual(sealedView.refused, ['navigation', 'navigation', 'navigation'], 'each told (the window tells a kind once)');
    }
});

test('no drag region the page draws is the window’s: a user sheet makes every element of each of its documents no-drag', async () => {
    const view = await sealed(demoPage);
    call(view.on.get('dom-ready'));
    call(view.on.get('dom-ready'));
    assert.deepEqual(
        view.calls.filter((name) => name.startsWith('insertCSS:')),
        [`insertCSS:user:${noDragSheet}`, `insertCSS:user:${noDragSheet}`],
    );
    assert.equal(noDragSheet, '*, *::before, *::after { -webkit-app-region: no-drag !important; }');
    // Our switch keeps its own: its strip is the window's drag region, its control is not.
    const ours = await sealed(demoSwitch);
    assert.equal(ours.on.has('dom-ready'), false);
});

// ---- When the page's view enters the window ------------------------------------------------------------------------------

// The page's own script makes its strip a drag region as it runs, before the desktop's sheet that makes no drag region of
// the page the window's can be put in; a view out of the window answers no hit test. So the page's view enters the
// window only once its document's sheets have settled, under ours, or at a bound whatever happened. Nothing of it reads
// the platform: the order is the same on Windows, macOS and Linux.

const flush = () => new Promise<void>((resolve) => setImmediate(resolve));

test('the page’s view is told its document’s sheets settled only once every one has, landed or refused, at each document', async () => {
    const made = stand();
    const pending: (() => void)[] = [];
    let refuse = false;
    (made.contents as unknown as { insertCSS: (css: string) => Promise<string> }).insertCSS = (css) => {
        made.calls.push(`insertCSS:${css}`);
        return new Promise((resolve, reject) => pending.push(() => (refuse ? reject(new Error('The document is gone.')) : resolve('key'))));
    };
    let told = 0;
    await sealView(made.contents, demoPage, { read: () => pageBytes, refused: () => undefined, sheet: 'p { color: red; }', landed: () => void told++ });
    await flush();
    assert.equal(told, 0, 'nothing before a document');
    call(made.on.get('dom-ready'));
    assert.deepEqual(made.calls.filter((name) => name.startsWith('insertCSS:')), [`insertCSS:${noDragSheet}`, 'insertCSS:p { color: red; }']);
    pending.shift()!();
    await flush();
    assert.equal(told, 0, 'not while a sheet is still out');
    pending.shift()!();
    await flush();
    assert.equal(told, 1, 'once both have landed');
    // The next document's sheets are refused: settled all the same, so it is told.
    refuse = true;
    call(made.on.get('dom-ready'));
    for (const run of pending.splice(0)) run();
    await flush();
    assert.equal(told, 2);
    // A view with no such hook is told nothing and keeps the events it had: ours has no dom-ready at all.
    const ours = await sealed(demoSwitch);
    assert.equal(ours.on.has('dom-ready'), false);
});

/** `demoPageShowing` on a window and a clock of the test's own: what was added and taken off, and the timers set. */
function showingRig() {
    const done: string[] = [];
    const timers = new Map<number, () => void>();
    let made = 0;
    const showing = demoPageShowing({
        add: () => void done.push('add'),
        remove: () => void done.push('remove'),
        setTimer: (run, ms) => {
            assert.equal(ms, demoPageBoundMs);
            timers.set(++made, run);
            return made;
        },
        clearTimer: (timer) => void timers.delete(timer as number),
    });
    const bound = () => {
        const due = [...timers.values()];
        timers.clear();
        for (const run of due) run();
    };
    return { showing, done, timers, bound };
}

test('the page’s view enters the window once its document’s sheets have settled, once, and its bound is cleared then', () => {
    assert.equal(demoPageBoundMs, 1500);
    const { showing, done, timers } = showingRig();
    showing.loading();
    assert.deepEqual(done, [], 'nothing added before the sheets settle');
    assert.equal(timers.size, 1, 'the bound runs from the load');
    showing.landed();
    assert.deepEqual(done, ['add']);
    assert.equal(timers.size, 0, 'the bound cleared when the sheets came first');
    showing.landed();
    assert.deepEqual(done, ['add'], 'added once');
});

test('a sheet that never settles, or a load that fails and brings no document: the view is in the window at the bound', () => {
    const { showing, done, timers, bound } = showingRig();
    showing.loading();
    // No `landed` comes: a sheet whose promise never settles, a dom-ready that never comes, a load that failed.
    bound();
    assert.deepEqual(done, ['add']);
    assert.equal(timers.size, 0);
    showing.landed();
    assert.deepEqual(done, ['add'], 'a late settle adds nothing more');
});

test('a reload takes the view off and brings it back when the new document’s sheets settle, or at the bound', () => {
    const { showing, done, timers, bound } = showingRig();
    showing.loading();
    showing.landed();
    showing.loading();
    assert.deepEqual(done, ['add', 'remove'], 'off until the new document’s sheets settle');
    assert.equal(timers.size, 1);
    showing.landed();
    assert.deepEqual(done, ['add', 'remove', 'add']);
    assert.equal(timers.size, 0);
    showing.loading();
    bound();
    assert.deepEqual(done, ['add', 'remove', 'add', 'remove', 'add']);
    // A reload before the view was shown takes nothing off and starts its bound again.
    const early = showingRig();
    early.showing.loading();
    early.showing.loading();
    assert.deepEqual(early.done, []);
    assert.equal(early.timers.size, 1, 'one bound, the newest');
});

test('the window’s close leaves no timer, and nothing is added after it', () => {
    const { showing, done, timers, bound } = showingRig();
    showing.loading();
    showing.close();
    assert.equal(timers.size, 0, 'the bound cleared at the close');
    showing.landed();
    showing.loading();
    bound();
    assert.deepEqual(done, []);
});

test('the window adds ours before the loads and the page under it only through the showing, on every system alike', () => {
    const window = readFileSync(join(root, 'src', 'demo-window.ts'), 'utf8');
    // The page's view is added at one place, at index 0, so ours stays drawn over it; ours is added before the loads.
    assert.equal(window.split('addChildView(page').length - 1, 1, 'one add of the page');
    assert.ok(window.includes('        add: () => window.contentView.addChildView(page, 0),\n'));
    assert.ok(window.includes('        remove: () => window.contentView.removeChildView(page),\n'));
    assert.ok(window.includes('        setTimer: (run, ms) => setTimeout(run, ms),\n'));
    assert.ok(window.indexOf('window.contentView.addChildView(ours);') < window.indexOf('showing.loading();'));
    // The page's view, and not ours, is told its sheets settled.
    const pageSeal = window.slice(window.indexOf('await sealView(page.webContents, demoPage, {'), window.indexOf('await sealView(ours.webContents, demoSwitch, {'));
    const oursSeal = window.slice(window.indexOf('await sealView(ours.webContents, demoSwitch, {'), window.indexOf("ours.setBackgroundColor('#00000000');"));
    assert.ok(pageSeal.includes('        landed: () => showing.landed(),\n'));
    assert.ok(!oursSeal.includes('landed'));
    // The bound runs from the start of the views' load, after the band's sheet on macOS and outside its branch: the same
    // statement on every system.
    assert.ok(window.includes('        }\n        showing.loading();\n        await Promise.all([page.webContents.loadURL(demoPage.url), ours.webContents.loadURL(demoSwitch.url)]);'));
    // Our reload takes the view off before the page loads again; the close clears the bound.
    assert.ok(window.includes('        reload: () => {\n            showing.loading();\n            page.webContents.reload();\n        },'));
    assert.ok(window.includes('    const close = () => {\n        showing.close();\n'));
    assert.ok(window.includes("    window.on('closed', close);"));
    // Nothing that decides when the page is shown reads the platform.
    const demo = readFileSync(join(root, 'src', 'demo-page.ts'), 'utf8');
    const helper = demo.slice(demo.indexOf('export function demoPageShowing('), demo.indexOf('\n}\n', demo.indexOf('export function demoPageShowing(')));
    assert.ok(helper.length > 0 && !/platform/.test(helper));
    const wiring = window.slice(window.indexOf('const showing = demoPageShowing({'), window.indexOf('});', window.indexOf('const showing = demoPageShowing({')));
    assert.ok(wiring.length > 0 && !/platform/.test(wiring));
});

// ---- Before Electron is ready: the Demo's schemes, and no host name resolved --------------------------------------------

test('a Demo start registers the two schemes and maps every host name to nothing before Electron is ready', () => {
    const done: unknown[] = [];
    demoBeforeReady(
        { appendSwitch: (name: string, value?: string) => void done.push(['switch', name, value]) },
        { registerSchemesAsPrivileged: (schemes: unknown) => void done.push(['schemes', schemes]) },
    );
    assert.deepEqual(demoResolverRule, ['host-resolver-rules', 'MAP * ~NOTFOUND']);
    assert.deepEqual(done, [
        ['switch', 'host-resolver-rules', 'MAP * ~NOTFOUND'],
        ['schemes', demoSchemes()],
    ]);
});

test('the host-resolver rule is set by a Demo start alone, before ready, and a Live start never sets it', () => {
    const main = readFileSync(join(root, 'src', 'main.ts'), 'utf8');
    const start = main.slice(main.indexOf('function startDesktop('));
    const set = start.indexOf("if (runOn.mode === 'demo') demoBeforeReady(app.commandLine, protocol);");
    assert.ok(set > 0, 'the Demo start sets it');
    const ready = start.indexOf('void app.whenReady().then(() => (');
    assert.ok(ready > 0 && set < ready, 'before ready');
    assert.equal(main.split('demoBeforeReady(').length - 1, 1, 'once, in the Demo start');
    assert.ok(!/appendSwitch|host-resolver-rules|registerSchemesAsPrivileged/.test(main), 'nothing else in main.ts sets a switch or a scheme');
    // Live is a start of its own: the relaunch hands it its arguments, never the switches this run appended.
    assert.ok(main.includes('app.relaunch(relaunchOptions(process.argv,'));
});

// ---- Our switch's place and page --------------------------------------------------------------------------------------

test('the page’s strip is fixed: right of its 284-pixel sidebar, 38 high; on macOS the window’s band of 26 lies under the page', () => {
    assert.deepEqual(demoGeometry, { sidebar: 284, strip: 38, macBand: 26, macCut: 177 });
    assert.equal(demoGeometry.macBand, titleBand, 'the band is the window frame’s own');
    assert.equal(demoGeometry.strip, stripHeight, 'the strip is as high as the caption buttons on Windows');
});

test('on Windows our switch stands left of the caption buttons over the strip’s right end, read from the overlay’s geometry', () => {
    const page = readFileSync(join(root, 'shell', 'demo-switch.html'), 'utf8');
    const css = page.replace(/\s+/g, ' ');
    // The view is told the free title bar area in its own coordinates; where there is no overlay it is all of it.
    assert.ok(
        /body \{[^}]*padding: 0 12px 0 16px; padding-right: calc\(12px \+ 100vw - env\(titlebar-area-x, 0px\) - env\(titlebar-area-width, 100vw\)\);/.test(css),
        'the right padding leaves the buttons their width',
    );
});

test('elsewhere than macOS the page fills the content and our switch covers the strip, at every width the window allows', () => {
    for (const platform of ['win32', 'linux'] as const) {
        assert.deepEqual(demoLayout(platform, 1440, 900), { page: { x: 0, y: 0, width: 1440, height: 900 }, switch: { x: 284, y: 0, width: 1156, height: 38 }, band: null, cut: null });
        assert.deepEqual(demoLayout(platform, 900, 600), { page: { x: 0, y: 0, width: 900, height: 600 }, switch: { x: 284, y: 0, width: 616, height: 38 }, band: null, cut: null });
    }
    // Narrower than the sidebar, which the window's least width never is, the switch has no width rather than a negative one.
    assert.deepEqual(demoLayout('linux', 200, 100).switch, { x: 284, y: 0, width: 0, height: 38 });
});

test('on macOS the page and our switch start at the window’s top edge, over our band, which drags the top 26 left of the switch’s span', () => {
    assert.deepEqual(demoLayout('darwin', 1440, 900), {
        page: { x: 0, y: 0, width: 1440, height: 900 },
        switch: { x: 284, y: 0, width: 1156, height: 38 },
        band: { x: 0, y: 0, width: 1440, height: 26 },
        cut: { x: 1263, y: 0, width: 177, height: 26 },
    });
    assert.deepEqual(demoLayout('darwin', 900, 600), {
        page: { x: 0, y: 0, width: 900, height: 600 },
        switch: { x: 284, y: 0, width: 616, height: 38 },
        band: { x: 0, y: 0, width: 900, height: 26 },
        cut: { x: 723, y: 0, width: 177, height: 26 },
    });
    // The page and the switch stand where they stand on Windows and Linux, to the pixel.
    for (const [width, height] of [[900, 600], [1440, 900], [2600, 1600]] as const) {
        const mac = demoLayout('darwin', width, height);
        for (const platform of ['win32', 'linux'] as const) {
            const other = demoLayout(platform, width, height);
            assert.deepEqual([mac.page, mac.switch], [other.page, other.switch], `${platform} ${width}`);
        }
    }
});

// The control lies inside the span cut out of the band by construction: the switch's own stylesheet fixes its greatest
// extent (a width it is clipped to, its offset from the right edge, its shadow), whatever font draws its words, and the
// span is those together, from one definition the layout and the band's sheet both use. Everything left of the span
// drags by the window's own contents: over the sidebar and over the Demo strip.
test('on macOS the span cut out of the band holds the switch at its greatest extent, and the band drags left of it, at every width', () => {
    assert.deepEqual(demoSwitchMost, { width: 160, offset: 12, shadow: 5 });
    assert.equal(demoGeometry.macCut, demoSwitchMost.width + demoSwitchMost.offset + demoSwitchMost.shadow);
    // The switch page's own numbers, read from its stylesheet: a change there that the span does not hold turns this red.
    const ours = readFileSync(join(root, 'shell', 'demo-switch.html'), 'utf8').replace(/\s+/g, ' ');
    const rule = /\.switch \{([^}]*)\}/.exec(ours)?.[1] ?? '';
    const segments = /\.switch a, \.switch span \{([^}]*)\}/.exec(ours)?.[1] ?? '';
    const ring = /\.switch a:focus-visible \{([^}]*)\}/.exec(ours)?.[1] ?? '';
    const most = Number(/max-width: (\d+)px;/.exec(rule)?.[1]);
    const shadow = Number(/box-shadow: 0 0 0 (\d+)px #fff4d6;/.exec(rule)?.[1]);
    const offset = Number(/body \{[^}]*padding-right: calc\((\d+)px \+ 100vw - env\(titlebar-area-x, 0px\) - env\(titlebar-area-width, 100vw\)\);/.exec(ours)?.[1]);
    // The box is at most that wide, its padding inside it, and its segments shrink to fit and hide what would not, so
    // no font draws the control wider; beyond the box only the shadow is drawn, the focus ring within it.
    assert.ok(/box-sizing: border-box;/.test(rule) && /padding: 2px;/.test(rule), 'the greatest width is the box’s, its padding inside');
    assert.ok(/min-width: 0; overflow: hidden;/.test(segments), 'the segments shrink and clip their words');
    const ringOut = Number(/outline: (\d+)px solid/.exec(ring)?.[1]) + Number(/outline-offset: (\d+)px;/.exec(ring)?.[1]) - 2;
    assert.ok(most > 0 && shadow > 0 && offset > 0 && ringOut <= shadow, 'each number is read, and the ring stays within the shadow');
    assert.ok(demoGeometry.macCut >= most + offset + shadow, `the span (${demoGeometry.macCut}) holds the control at its greatest extent`);
    // Well above what the control measures today, in Segoe UI: its two segments, its padding.
    assert.ok(most >= 1.5 * demoStrip.ours.normal, 'its greatest width is half again what it measures');
    // Inside the span the switch's page drags but for the switch.
    assert.ok(/html, body \{[^}]*-webkit-app-region: drag;/.test(ours));
    assert.ok(/\.switch \{ -webkit-app-region: no-drag;/.test(ours));
    for (const [width, height] of [[900, 600], [1440, 900]] as const) {
        const at = demoLayout('darwin', width, height);
        assert.deepEqual(at.cut, { x: width - demoGeometry.macCut, y: 0, width: demoGeometry.macCut, height: demoGeometry.macBand }, `${width}`);
        // What drags by the band: from the window's left edge to the span, over the sidebar and the Demo strip.
        const drags = { from: at.band!.x, to: at.cut!.x };
        assert.deepEqual(drags, { from: 0, to: width - 177 }, `${width}`);
        assert.ok(drags.to - demoGeometry.sidebar >= 400, `${width}: a long stretch of the Demo strip drags`);
    }
    // At the window's least width (900) the band still drags 439 pixels of the Demo strip.
    assert.equal(demoLayout('darwin', 900, 600).cut!.x - demoGeometry.sidebar, 439);
    // The band's sheet makes the cut its right span, from the same number; until it lands, the cut is the whole band.
    assert.equal(demoBandSheet, '.cut { left: auto !important; width: 177px !important; }');
    const band = readFileSync(join(root, 'shell', 'demo-band.html'), 'utf8').replace(/\s+/g, ' ');
    assert.ok(band.includes(`.cut { position: absolute; top: 0; left: 0; right: 0; height: ${demoGeometry.macBand}px; -webkit-app-region: no-drag; }`));
    // Windows and Linux have no band and no span.
    for (const platform of ['win32', 'linux'] as const) assert.equal(demoLayout(platform, 900, 600).cut, null);
});

test('on macOS the page’s view gets a sheet that moves its sidebar’s contents below the traffic lights; elsewhere its sheet is as it was', () => {
    assert.equal(demoMacSheet, '.jc-console .side { padding-top: 26px !important; }');
    assert.equal(demoPageSheet('darwin'), `${demoMacSheet} ${demoLinkSheet}`);
    // Windows and Linux: the strip's sheet and the link's, the same string as before the Mac's sheet was added.
    for (const platform of ['win32', 'linux'] as const) {
        assert.equal(demoPageSheet(platform), `${demoStripSheets(platform)!.page} ${demoLinkSheet}`);
        assert.ok(!demoPageSheet(platform).includes('.jc-console .side'));
    }
    // The page as committed still draws its sidebar by that selector, an aside of class "side" 284 wide, laid out in
    // border boxes, so the padding moves the contents down and keeps the sidebar's height and ground.
    const demo = readFileSync(join(root, 'demo', 'index.html'), 'utf8');
    assert.ok(demo.includes('<aside class="side" id="side">'));
    assert.ok(demo.includes('.jc-console .side{background:var(--side-bg);border-right:1px solid var(--line);flex-direction:column;flex:0 0 284px;width:284px;'));
    assert.ok(demo.includes('.jc-console,.jc-console *{box-sizing:border-box}'));
    // Every rule of the page for a Mac window, keyed on a class this view never sets: the sidebar's padding, which our
    // sheet gives, and a drag region over the sidebar's top 284 by 26, which it leaves out, since no drag region of the
    // page is the window's (noDragSheet) and our band drags those same pixels. A page that changes them turns this red.
    assert.deepEqual(
        [...demo.matchAll(/[^{}]*desktop-mac[^{}]*\{[^{}]*\}/g)].map((match) => match[0]),
        [
            'html.desktop-mac .jc-console .side{padding-top:26px}',
            'html.desktop-mac .jc-console #title-drag{z-index:30;-webkit-app-region:drag;width:284px;height:26px;position:fixed;top:0;left:0}',
        ],
    );
    assert.equal(demo.split('desktop-mac').length - 1, 2, 'the class is named nowhere else, by no script either');
    const mac = demoLayout('darwin', 900, 600);
    assert.ok(mac.band!.x === 0 && mac.band!.y === 0 && mac.cut!.x > 284 && mac.band!.height === 26, 'the band drags the page’s own drag region for a Mac window, and more');
    // Its strip starts at the top of its view, right of the sidebar, and holds no control once the embedding script has
    // taken its switch out, so the band's drag region over its top 26 pixels takes no click meant for the page.
    assert.ok(demo.includes('.jc-console #mode-bar{box-sizing:border-box;z-index:20;white-space:nowrap;-webkit-app-region:drag;align-items:center;gap:8px;height:38px;padding:0 12px 0 16px;font-size:13px;display:flex;position:fixed;top:0;left:284px;right:0}'));
    assert.ok(demo.includes("document.querySelectorAll('#mode-bar .mb-seg, #mode-bar .mb-oi, #mode-bar .sd-note').forEach(function (each) {"));
});

test('our switch’s page holds no script, links only to the choice of Live, and is the window’s drag region but for the switch', () => {
    const page = readFileSync(join(root, 'shell', 'demo-switch.html'), 'utf8');
    assert.ok(!/<script|on[a-z]+\s*=|javascript:/i.test(page), 'no script, no handler');
    assert.deepEqual([...page.matchAll(/href="([^"]*)"/g)].map((match) => match[1]), ['jason-switch://switch/live']);
    assert.ok(!/src=|url\(|@import/i.test(page), 'it loads nothing');
    const css = page.replace(/\s+/g, ' ');
    assert.ok(/html, body \{[^}]*-webkit-app-region: drag;/.test(css), 'the strip drags the window');
    assert.ok(/\.switch \{[^}]*-webkit-app-region: no-drag;/.test(css), 'the switch takes the click');
    // The page's strip shows through with its own dot and words: ours draws the switch alone, at the strip's right end,
    // on the strip's own colour, so the tail of a sentence it stands over is covered rather than drawn through.
    assert.ok(/html, body \{[^}]*background: transparent;/.test(css), 'the strip’s ground is the page’s own');
    assert.ok(/body \{[^}]*justify-content: flex-end;/.test(css), 'the switch at the right end');
    assert.ok(/\.switch \{[^}]*background: linear-gradient\(rgba\(20, 33, 43, \.08\), rgba\(20, 33, 43, \.08\)\), #fff4d6;[^}]*box-shadow: 0 0 0 5px #fff4d6;/.test(css), 'on the Demo strip’s colour');
    const body = page.slice(page.indexOf('<body>'), page.indexOf('</body>'));
    assert.deepEqual([...body.matchAll(/<(\w+)[\s>]/g)].map((match) => match[1]), ['body', 'nav', 'span', 'a'], 'the switch and nothing else: no dot, no sentence');
    assert.ok(!/Open in/.test(body), 'the Demo has nothing to open');
});

test('our switch’s view is transparent, so the page’s strip shows through it', () => {
    const window = readFileSync(join(root, 'src', 'demo-window.ts'), 'utf8');
    assert.ok(window.includes("ours.setBackgroundColor('#00000000');"));
    assert.ok(window.indexOf("ours.setBackgroundColor('#00000000');") < window.indexOf('window.contentView.addChildView(ours);'), 'before it is shown');
});

// The band's document is the window's own contents, which fill the whole window under the two views, and Electron asks
// the window's own contents first whether a point is in a drag region, whichever view is drawn over it: so only the band's
// own height drags, never the document as a whole, and never where our switch stands. The page covers it whole;
// what it paints before the page is shown is the page's own ground, never the Demo strip's yellow.
test('our band on macOS holds no script and nothing to click, is the page’s own ground, and drags over its own height alone, never where our switch stands', () => {
    const page = readFileSync(join(root, 'shell', 'demo-band.html'), 'utf8');
    assert.ok(!/<script|on[a-z]+\s*=|javascript:|href=|<a\b|<button|<input/i.test(page), 'no script and nothing to follow');
    assert.ok(!/src=|url\(|@import/i.test(page), 'it loads nothing');
    const css = page.replace(/\s+/g, ' ');
    assert.ok(/background: linear-gradient\(to right, #f3f5f9 0 284px, #fff 284px\);/.test(css));
    // The two colours are the page's own: its sidebar's and its ground's, never the strip's yellow.
    const demo = readFileSync(join(root, 'demo', 'index.html'), 'utf8');
    assert.equal(/--side-bg:(#[0-9a-f]+)/.exec(demo)?.[1], '#f3f5f9');
    assert.equal(/--bg:(#[0-9a-f]+)/.exec(demo)?.[1], '#fff');
    assert.ok(!/#fff4d6/i.test(page), 'no yellow in the band');
    assert.ok(/html \{[^}]*\}/.test(css) && !/html \{[^}]*-webkit-app-region/.test(css), 'the document as a whole carries no drag region');
    assert.equal(css.split('-webkit-app-region').length - 1, 2, 'one drag region and its cut-out');
    assert.ok(css.includes(`body { margin: 0; height: ${demoGeometry.macBand}px; position: relative; -webkit-app-region: drag; }`), 'the body drags, as high as the band');
    assert.ok(
        css.includes(`.cut { position: absolute; top: 0; left: 0; right: 0; height: ${demoGeometry.macBand}px; -webkit-app-region: no-drag; }`),
        'until the desktop’s sheet narrows it to the switch’s span, none of it drags',
    );
    const body = page.slice(page.indexOf('<body>'), page.indexOf('</body>') + '</body>'.length);
    assert.equal(body, '<body><div class="cut"></div></body>', 'the body holds the cut-out and nothing else');
});

test('the Demo window takes the window’s own frame, loads our band under its views on macOS, and elsewhere keeps its frame', () => {
    const main = readFileSync(join(root, 'src', 'main.ts'), 'utf8');
    const demo = main.slice(main.indexOf('async function runDemo('), main.indexOf('function keepElectronUnderData('));
    assert.ok(demo.includes('...frameOf(process.platform, nativeTheme.shouldUseDarkColors),'));
    assert.ok(!/demoMacFrame/.test(main));
    const window = readFileSync(join(root, 'src', 'demo-window.ts'), 'utf8');
    // The band is loaded, then narrowed by its sheet, before either view is loaded.
    const band = window.indexOf('await window.webContents.loadFile(bandFile);');
    const sheet = window.indexOf("await window.webContents.insertCSS(demoBandSheet, { cssOrigin: 'user' });");
    const views = window.indexOf('await Promise.all([page.webContents.loadURL(demoPage.url), ours.webContents.loadURL(demoSwitch.url)]);');
    assert.ok(band > 0 && band < sheet && sheet < views);
    assert.ok(window.slice(window.lastIndexOf("if (deps.platform === 'darwin') {", band), band).length < 60, 'on macOS alone');
    assert.equal(window.split('insertCSS(').length - 1, 1, 'the band’s sheet is the window’s one');
    assert.ok(window.includes('const at = demoLayout(deps.platform, width, height);'));
});

// ---- What the Demo reaches --------------------------------------------------------------------------------------------

/** The modules a source file imports, by their names under src/. */
function importsOf(file: string): string[] {
    const text = readFileSync(join(root, 'src', file), 'utf8');
    return [...text.matchAll(/^import\s[^;]*?from\s+'([^']+)'/gm)].map((match) => match[1] ?? '');
}

test('the Demo’s two modules reach no jason, no runtime, no program, no process and nothing of the earlier demo', () => {
    const refused = /^\.\/(cli|programs|installation|own-runtime|chat.*|setup|repair|window-pairing|browser-bridge|ipc|preload|demo-(over|runtime|folders))\.ts$|child_process|^node:net$|^node:http/;
    for (const file of ['demo-page.ts', 'demo-window.ts']) {
        const imports = importsOf(file);
        assert.ok(imports.length > 0, file);
        for (const name of imports) assert.ok(!refused.test(name), `${file} imports ${name}`);
    }
    assert.deepEqual(importsOf('demo-page.ts'), ['electron', './window-frame.ts']);
    assert.deepEqual(importsOf('demo-window.ts'), ['electron', 'node:fs', 'node:path', './demo-page.ts']);
    // demo-page.ts takes Electron's types alone, so its code runs here without Electron.
    assert.match(readFileSync(join(root, 'src', 'demo-page.ts'), 'utf8'), /^import type \{[^}]*\} from 'electron';$/m);
});

// ---- The page as it was built, and the addresses it holds -------------------------------------------------------------

/** The counted list of the addresses the page holds (`demo/addresses.txt`): its pattern, and each exact string's count and reason. */
interface AddressList {
    pattern: RegExp;
    entries: { text: string; count: number; why: string }[];
}

function addressList(text: string): AddressList {
    const lines = text.split('\n').filter((line) => line.trim() !== '' && !line.startsWith('#'));
    const patterns = lines.filter((line) => line.startsWith('pattern\t'));
    assert.equal(patterns.length, 1, 'one pattern line');
    const entries = lines
        .filter((line) => !line.startsWith('pattern\t'))
        .map((line) => {
            const [count, found, why, ...rest] = line.split('\t');
            assert.equal(rest.length, 0, line);
            assert.match(count ?? '', /^[1-9][0-9]*$/, line);
            return { text: found ?? '', count: Number(count), why: why ?? '' };
        });
    return { pattern: new RegExp(patterns[0]!.slice('pattern\t'.length), 'g'), entries };
}

/** Every text of the page the list's pattern matches, with how many times it stands there. */
function addressesIn(page: string, pattern: RegExp): Map<string, number> {
    const found = new Map<string, number>();
    for (const match of page.matchAll(pattern)) found.set(match[0], (found.get(match[0]) ?? 0) + 1);
    return found;
}

/** What differs between the page and its list: a string not listed, a count other than the listed one, a listed string gone. */
function scan(page: string, list: AddressList): string[] {
    const found = addressesIn(page, list.pattern);
    const listed = new Map(list.entries.map((entry) => [entry.text, entry.count]));
    const findings: string[] = [];
    for (const [text, count] of found) {
        if (!listed.has(text)) findings.push(`not listed: ${text} (${count})`);
        else if (listed.get(text) !== count) findings.push(`${text}: the page holds ${count}, the list says ${listed.get(text)}`);
    }
    for (const [text, count] of listed) if (!found.has(text)) findings.push(`${text}: the list says ${count}, the page holds none`);
    return findings;
}

const pageText = () => readFileSync(join(root, 'demo', 'index.html'), 'utf8');
const listed = () => addressList(readFileSync(join(root, 'demo', 'addresses.txt'), 'utf8'));

test('the page holds exactly the addresses its list counts, each once in the list and each with its reason', () => {
    const list = listed();
    assert.deepEqual(scan(pageText(), list), []);
    assert.equal(new Set(list.entries.map((entry) => entry.text)).size, list.entries.length, 'each string once');
    for (const entry of list.entries) {
        assert.match(entry.text, new RegExp(`^${list.pattern.source}$`), `${entry.text} is of the pattern's shape`);
        assert.ok(entry.why.trim().length > 10, `${entry.text} says why it is inert`);
    }
    // The bare scheme, a placeholder with no host, is listed like any other.
    assert.ok(list.entries.some((entry) => entry.text === 'https://'));
});

test('the counted scan refuses an address not listed, one more or one fewer of a listed one, and a scheme of any name', () => {
    const page = pageText();
    const list = listed();
    assert.deepEqual(scan(`${page}<link rel="dns-prefetch" href="https://names.example.net">`, list), ['not listed: https://names.example.net (1)']);
    assert.deepEqual(scan(`${page}https://api.deepseek.com`, list), ['https://api.deepseek.com: the page holds 2, the list says 1']);
    assert.deepEqual(scan(page.replace('https://api.deepseek.com', 'about:blank'), list), ['https://api.deepseek.com: the list says 1, the page holds none']);
    assert.deepEqual(scan(`${page}ws://127.0.0.1`, list), ['not listed: ws://127.0.0.1 (1)']);
    assert.deepEqual(scan(`${page}x-custom+scheme://host`, list), ['not listed: x-custom+scheme://host (1)']);
});

test('the page is the file its README names, by its digest: a built page, received as it is and never edited', () => {
    const readme = readFileSync(join(root, 'demo', 'README.md'), 'utf8');
    const named = /SHA-256 `([0-9a-f]{64})`/.exec(readme)?.[1];
    assert.ok(named, 'the README names the page by its SHA-256');
    assert.equal(createHash('sha256').update(readFileSync(join(root, 'demo', 'index.html'))).digest('hex'), named);
    assert.ok(readme.includes('(addresses.txt)'), 'the README points at the counted list');
});

test('every request from the page’s view to an address the page holds, or to any other, is refused before it leaves', async () => {
    const view = await sealed();
    const verdict = (url: string) => {
        let answer: { cancel?: boolean } | undefined;
        call(view.handler('onBeforeRequest'), { url }, (response: { cancel?: boolean }) => (answer = response));
        return answer?.cancel;
    };
    const urls: string[] = [];
    for (const entry of listed().entries) {
        const rest = entry.text.slice(entry.text.indexOf('://') + 3);
        for (const scheme of ['http', 'https', 'ws', 'wss']) urls.push(`${scheme}://${rest === '' ? 'localhost' : rest}/x`);
    }
    urls.push(
        'http://127.0.0.1:5000/v1/system.info',
        'file:///C:/Users/someone/.jason/config/settings.json',
        'ftp://example.com/',
        'jason-switch://switch/live',
        // A blob of the page's own opaque origin, the page's own scheme at another file, with a query and at another
        // host, a scheme of nobody's, and Chromium's own pages.
        'blob:null/0f0e0d0c-0b0a-0908-0706-050403020100',
        'jason-demo://page/other.html',
        'jason-demo://page/index.html?x',
        'jason-demo://other/index.html',
        'x-custom://host/',
        'chrome://gpu',
    );
    for (const url of urls) {
        assert.equal(requestAllowed(demoPage, url), false, url);
        assert.equal(verdict(url), true, url);
    }
    // And a request that got past the filter meets a scheme that answers nothing.
    for (const url of urls.filter((url) => /^(https?|file):/.test(url))) {
        const scheme = url.slice(0, url.indexOf(':'));
        const answer = (await call(view.protocols.get(scheme), { url, method: 'GET' })) as Response;
        assert.equal(answer.status, 403, url);
    }
});

// ---- The strip on Windows and Linux, where the caption buttons stand over its right end ---------------------------------

// The caption buttons' width, in the view's own pixels, as the overlay's geometry gives it at a scale of 100% and of 150%:
// on Windows, and on Linux as GNOME draws them, and at 100% as Plasma does.
const captions = [138, 137.33];
const linuxCaptions = [114, 113.33, 72];

test('the page’s strip, as the desktop’s sheet names it, is still in the page: the selectors, the strip’s padding and its breakpoint', () => {
    const page = pageText();
    const sheets = demoStripSheets('win32');
    assert.ok(sheets);
    const named = new Set([...sheets.page.matchAll(/\.jc-console [#.][\w-]+/g)].map((match) => match[0]));
    assert.deepEqual([...named].sort(), ['.jc-console #mode-bar', '.jc-console .mb-long', '.jc-console .mb-short']);
    for (const selector of named) assert.ok(page.includes(`${selector}{`), `${selector} is a rule of the page`);
    // The numbers the sheet is computed from are the page's own: its strip's left edge and padding, the gap before its
    // empty right-hand slot, and the width at which it takes its short sentence.
    assert.ok(page.includes('.jc-console #mode-bar{box-sizing:border-box;z-index:20;white-space:nowrap;-webkit-app-region:drag;align-items:center;gap:8px;height:38px;padding:0 12px 0 16px;'));
    assert.ok(page.includes('left:284px;right:0}'));
    assert.ok(page.includes('@media (width<=1100px){.jc-console .mb-long{display:none}.jc-console .mb-short{display:inline}}'));
    assert.ok(page.includes('<span class="mb-long">${long}</span><span class="mb-short">${short}</span>'));
    assert.ok(page.includes('.jc-console #mode-bar>span{text-overflow:ellipsis;overflow:hidden}'), 'a sentence that does not fit ends in an ellipsis of the page’s own');
    // The two sentences whose ends the numbers are measured from, as the page states them.
    assert.ok(page.includes("say('an example of what you can build on Jason. The data is samples, and nothing is sent.','an example of what you can build. Nothing is sent.')"));
});

test('on Windows and Linux the sheets are the desktop’s: the strip inset by the caption buttons’ width read from the overlay, never a number', () => {
    const sheets = demoStripSheets('win32');
    assert.ok(sheets);
    // Linux takes the same sheets, but for the font of the switch's words: Segoe UI is a Windows font, so on Linux the
    // words stay in the system's own font, the switch page's.
    const font = '.switch a, .switch span { font-family: "Segoe UI", sans-serif !important; } ';
    assert.deepEqual(demoStripSheets('linux'), { page: sheets.page, switch: sheets.switch.replace(font, '') });
    assert.doesNotMatch(demoStripSheets('linux')!.switch, /font/);
    const caption = '100vw - env(titlebar-area-x, 0px) - env(titlebar-area-width, 100vw)';
    assert.ok(sheets.page.includes(`padding-right: calc(${demoStrip.inset}px + ${caption}) !important;`));
    assert.ok(sheets.page.includes(`container: jason-strip / inline-size !important;`));
    assert.ok(sheets.page.includes(`@container jason-strip (width < ${demoStrip.longFrom}px) { .jc-console .mb-long { display: none !important; } .jc-console .mb-short { display: inline !important; } }`));
    assert.ok(sheets.switch.includes('body { container: jason-switch / inline-size !important; }'));
    // The switch's widths are measured in Segoe UI, so on Windows its words are drawn in that font by name, not by
    // whatever the system's font is.
    assert.ok(sheets.switch.startsWith('.switch a, .switch span { font-family: "Segoe UI", sans-serif !important; } '));
    assert.ok(sheets.switch.includes(`@container jason-switch (width < ${demoStrip.tightBelow}px) { .switch a, .switch span { padding-left: ${demoStrip.ours.segment.tight}px !important; padding-right: ${demoStrip.ours.segment.tight}px !important; } }`));
    // Only the segments' inner padding changes: the words, the height and the rest of the switch stay.
    assert.ok(!/font|height|line-height|content:/.test(sheets.switch.slice(sheets.switch.indexOf('@container'))));
    assert.equal(demoStrip.ours.segment.normal, 12);
    assert.ok(/\.switch a, \.switch span \{[^}]*padding: 2px 12px;/.test(readFileSync(join(root, 'shell', 'demo-switch.html'), 'utf8').replace(/\s+/g, ' ')));
});

test('on Windows from 1101 to 1149 pixels the page shows its own short sentence, whole, clear of our switch', () => {
    for (const caption of captions) {
        for (let width = 1101; width <= 1149; width++) {
            const at = demoStripAt('win32', width, caption);
            assert.equal(at.form, 'short', `${width}`);
            assert.ok(at.whole, `${width} whole`);
            assert.ok(at.textEnd <= at.coveredFrom, `${width}: no word under the switch`);
        }
    }
    // Wide enough, the page's long sentence comes back, clear of the switch.
    const wide = demoStripAt('win32', 1440, 138);
    assert.equal(wide.form, 'long');
    assert.ok(wide.whole && wide.textEnd <= wide.coveredFrom);
});

test('on Windows from 900 to 933 pixels our switch takes its tighter form and the short sentence stands whole', () => {
    for (const caption of captions) {
        for (let width = 900; width <= 933; width++) {
            const at = demoStripAt('win32', width, caption);
            assert.equal(at.switchForm, 'tight', `${width}`);
            assert.equal(at.form, 'short');
            assert.ok(at.whole, `${width} whole`);
            assert.ok(at.textEnd <= at.coveredFrom, `${width}: no word under the switch`);
        }
    }
});

test('on Windows and Linux at every width from 900 up no word of the strip is under our switch, and the sentence is whole', () => {
    for (const [platform, widths] of [
        ['win32', captions],
        ['linux', linuxCaptions],
    ] as const) {
        for (const caption of widths) {
            for (let width = 900; width <= 2600; width++) {
                const at = demoStripAt(platform, width, caption);
                assert.ok(at.whole, `${platform} ${width} whole`);
                assert.ok(at.textEnd <= at.coveredFrom, `${platform} ${width}`);
            }
        }
    }
});

test('on macOS nothing changes: no sheet, the page’s own sentence, the switch in its own form at every width', () => {
    assert.equal(demoStripSheets('darwin'), null);
    for (let width = 900; width <= 2600; width++) {
        const at = demoStripAt('darwin', width, 0);
        assert.equal(at.form, width <= 1100 ? 'short' : 'long');
        assert.equal(at.switchForm, 'normal');
        assert.ok(at.whole && at.textEnd <= at.coveredFrom, `darwin ${width}`);
    }
    const window = readFileSync(join(root, 'src', 'demo-window.ts'), 'utf8');
    assert.ok(window.includes('const sheets = demoStripSheets(deps.platform);'));
    // On macOS the page's view gets the sidebar's sheet and the link's; elsewhere the strip's and the link's.
    assert.ok(window.includes('sheet: demoPageSheet(deps.platform),'));
    assert.ok(window.includes('sheet: sheets?.switch'));
});

test('a sheet given to the seal is put into every document of the view, of the user’s origin', async () => {
    const made = stand();
    await sealView(made.contents, demoSwitch, { read: () => pageBytes, refused: () => undefined, sheet: 'body { color: red; }' });
    call(made.on.get('dom-ready'));
    await Promise.resolve();
    assert.ok(made.calls.includes('insertCSS:user:body { color: red; }'));
    const page = stand();
    await sealView(page.contents, demoPage, { read: () => pageBytes, refused: () => undefined, sheet: 'p { color: red; }' });
    call(page.on.get('dom-ready'));
    await Promise.resolve();
    assert.ok(page.calls.includes(`insertCSS:user:${noDragSheet}`));
    assert.ok(page.calls.includes('insertCSS:user:p { color: red; }'));
});

test('the sheets’ three numbers are the ones the strip’s measures give, and the tight switch is the normal one less its segments’ padding', () => {
    assert.equal(demoStrip.inset, 79);
    assert.equal(demoStrip.longFrom, 641);
    assert.equal(demoStrip.tightBelow, 494);
    assert.equal(Math.round(demoStrip.ours.tight * 100) / 100, 70.16);
});

// ---- The repository's address in the page's note: the desktop's link, and the one address the view hands out -------

/**
 * The page's view sealed as the window seals it: the desktop's link sheet and script, the watch over the input that
 * reaches the view, and the reader that opens the one address.
 */
async function sealedPage() {
    const made = stand();
    const refused: DemoRefusal[] = [];
    const opened: string[] = [];
    const asks: string[] = [];
    const clock = { now: 1_000_000 };
    const gestures = watchDemoGestures(made.contents, () => clock.now);
    const opener = demoLinkOpener(() => opened.push(demoRepository), gestures, () => clock.now);
    const asked = (url: string) => {
        asks.push(url);
        return opener(url);
    };
    await sealView(made.contents, demoPage, { read: () => pageBytes, refused: (kind) => refused.push(kind), sheet: demoLinkSheet, script: demoLinkScript, asked });
    /** A person's click let go in the view, as Electron reports it to the main process. */
    const click = () => call(made.on.get('input-event'), event(), { type: 'mouseUp', button: 'left', clickCount: 1, x: 1, y: 1, modifiers: [] });
    /** Enter pressed in the view, as Electron reports it before the page sees it. */
    const enter = () => call(made.on.get('before-input-event'), event(), { type: 'keyDown', key: 'Enter', code: 'Enter', isAutoRepeat: false });
    return { ...made, refused, opened, asks, clock, click, enter };
}

const navigate = (view: Stand, name: string, url: string, isMainFrame = true) => {
    const stop = event({ url, isMainFrame });
    call(view.on.get(name), stop);
    return stop.stopped;
};

test('a person’s click, then the repository’s address: it opens in the browser once, and the view stays where it was', async () => {
    const view = await sealedPage();
    view.click();
    view.clock.now += 50;
    assert.ok(navigate(view, 'will-navigate', demoRepository), 'the navigation is refused');
    assert.deepEqual(view.opened, [demoRepository]);
    // Electron asks about the same navigation of the main frame a second time: the span opens it once.
    assert.ok(navigate(view, 'will-frame-navigate', demoRepository));
    assert.deepEqual(view.opened, [demoRepository]);
    assert.ok(!view.calls.some((name) => name.startsWith('loadURL:')), 'nothing is loaded in the view');
    // The person's own click is a choice, not a refusal.
    assert.deepEqual(view.refused, []);
});

test('the repository’s address with no click or Enter before it opens nothing: a page that asks on load, or on its own, is refused', async () => {
    const view = await sealedPage();
    for (let step = 0; step < 5; step++) {
        assert.ok(navigate(view, 'will-navigate', demoRepository));
        view.clock.now += demoLinkSpanMs + 1;
    }
    assert.deepEqual(view.opened, []);
    assert.ok(view.refused.length > 0 && view.refused.every((kind) => kind === 'navigation'));
});

test('a click or Enter opens the address within the gesture’s second, and after that second nothing', async () => {
    const inside = await sealedPage();
    inside.enter();
    inside.clock.now += demoLinkGestureMs;
    assert.ok(navigate(inside, 'will-navigate', demoRepository));
    assert.deepEqual(inside.opened, [demoRepository], 'Enter, then the address inside the second: it opens once');

    const after = await sealedPage();
    after.click();
    after.clock.now += demoLinkGestureMs + 1;
    assert.ok(navigate(after, 'will-navigate', demoRepository));
    assert.deepEqual(after.opened, [], 'the address after the second opens nothing');
    assert.deepEqual(after.refused, ['navigation']);
    assert.equal(demoLinkGestureMs, 1000);
});

test('a moment before the gesture opens nothing, and both moments are read from a monotonic clock', async () => {
    const view = await sealedPage();
    view.click();
    view.clock.now -= 1;
    assert.ok(navigate(view, 'will-navigate', demoRepository));
    assert.deepEqual(view.opened, [], 'a clock that stepped back keeps the gate shut');
    assert.deepEqual(view.refused, ['navigation']);
    // Without a clock given, the watch and the opener read performance.now(), which no step of the system's clock moves.
    const source = readFileSync(join(root, 'src', 'demo-page.ts'), 'utf8');
    assert.match(source, /const monotonic = \(\): number => performance\.now\(\);/);
    assert.match(source, /export function watchDemoGestures\(contents: Pick<WebContents, 'on'>, now: \(\) => number = monotonic\)/);
    assert.match(source, /export function demoLinkOpener\(open: \(\) => void, gestureAt: \(\) => number \| null, now: \(\) => number = monotonic\)/);
    assert.ok(!/Date\.now/.test(source.slice(source.indexOf('export const demoLinkGestureMs'))), 'no wall clock in the gate');
});

test('the same address again within the span opens nothing, a gesture before it or not; after the span a new gesture opens it again', async () => {
    const view = await sealedPage();
    view.click();
    navigate(view, 'will-navigate', demoRepository);
    view.clock.now += demoLinkSpanMs - 1;
    view.click();
    assert.ok(navigate(view, 'will-navigate', demoRepository));
    assert.deepEqual(view.opened, [demoRepository], 'a person clicking again and again opens one tab');
    view.clock.now += 1;
    view.click();
    assert.ok(navigate(view, 'will-navigate', demoRepository));
    assert.deepEqual(view.opened, [demoRepository, demoRepository]);
    assert.equal(demoLinkSpanMs, 2000);
});

test('only the main frame’s navigation is read: a frame’s within the page and a redirect are refused and ask nothing', async () => {
    const view = await sealedPage();
    view.click();
    assert.ok(navigate(view, 'will-frame-navigate', demoRepository, false));
    assert.ok(navigate(view, 'will-navigate', demoRepository, false));
    assert.ok(navigate(view, 'will-redirect', demoRepository, true));
    assert.ok(navigate(view, 'will-redirect', demoRepository, false));
    assert.deepEqual(view.asks, [], 'the reader is asked nothing');
    assert.deepEqual(view.opened, []);
    assert.ok(view.refused.length > 0 && view.refused.every((kind) => kind === 'navigation'));
});

test('any other address opens nothing and is refused, a gesture before it or not: another address, a path, a query, a fragment, another scheme', async () => {
    const view = await sealedPage();
    const others = [
        'https://github.com/reply-team/jason-ai/releases',
        'https://github.com/reply-team/jason-ai/',
        'https://github.com/reply-team/jason-ai?tab=readme',
        'https://github.com/reply-team/jason-ai#readme',
        'https://github.com/reply-team/jason-ai.git',
        'https://github.com/reply-team/jason-ai%2F',
        'https://github.com/reply-team',
        'https://github.com/',
        'http://github.com/reply-team/jason-ai',
        'ws://github.com/reply-team/jason-ai',
        'file://github.com/reply-team/jason-ai',
        'github.com/reply-team/jason-ai',
        'https://github.com.example.com/reply-team/jason-ai',
        'https://user@github.com/reply-team/jason-ai',
        'https://example.com/',
    ];
    for (const url of others) {
        view.click();
        for (const name of ['will-navigate', 'will-frame-navigate', 'will-redirect']) assert.ok(navigate(view, name, url), `${name} ${url} is refused`);
    }
    assert.deepEqual(view.opened, []);
    assert.ok(view.refused.length > 0 && view.refused.every((kind) => kind === 'navigation'), 'each is told as a refusal');
    // A pop-up to the address itself is denied and opens nothing: the view's window-open handler hands out no address.
    assert.deepEqual(call(view.handler('setWindowOpenHandler'), { url: demoRepository }), { action: 'deny' });
    assert.deepEqual(view.opened, []);
});

test('the opener compares the text it is handed whole, so any other text, the same letters in another case included, is not the address', () => {
    // Chromium hands a navigation's address over canonical (a host in lower case, no default port), so in a view these
    // reach the opener as the address itself; this row is about the comparison alone.
    const opened: string[] = [];
    const ask = demoLinkOpener(() => opened.push(demoRepository), () => 0, () => 0);
    for (const text of ['https://example.com/', 'jason-switch://switch/live', 'HTTPS://github.com/reply-team/jason-ai', 'https://GitHub.com/reply-team/jason-ai', 'https://github.com:443/reply-team/jason-ai', ' https://github.com/reply-team/jason-ai']) {
        assert.equal(ask(text), false, text);
    }
    assert.equal(ask(demoRepository), true);
    assert.deepEqual(opened, [demoRepository]);
});

test('a gesture is the left button let go or Enter pressed, nothing else', () => {
    assert.ok(isDemoLinkGesture({ type: 'mouseUp', button: 'left' }));
    assert.ok(isDemoLinkGesture({ type: 'keyDown', key: 'Enter' }));
    for (const input of [
        { type: 'mouseUp', button: 'right' },
        { type: 'mouseUp', button: 'middle' },
        { type: 'mouseUp' },
        { type: 'mouseDown', button: 'left' },
        { type: 'mouseMove' },
        { type: 'mouseWheel' },
        { type: 'keyDown', key: 'Enter', isAutoRepeat: true },
        { type: 'keyUp', key: 'Enter' },
        { type: 'keyDown', key: ' ' },
        { type: 'char', key: 'Enter' },
    ]) {
        assert.equal(isDemoLinkGesture(input), false, JSON.stringify(input));
    }
});

test('every document of the page’s view gets the link’s sheet, of the user’s origin, and the script, in a world of its own', async () => {
    const view = await sealedPage();
    call(view.on.get('dom-ready'));
    await Promise.resolve();
    assert.ok(view.calls.includes(`insertCSS:user:${noDragSheet}`));
    assert.ok(view.calls.includes(`insertCSS:user:${demoLinkSheet}`));
    assert.ok(view.calls.includes(`executeJavaScriptInIsolatedWorld:${demoLinkWorld}:${demoLinkScript}`));
    assert.notEqual(demoLinkWorld, 0, 'never the page’s own world');
    // Our switch's view carries no script.
    const switchView = await sealed(demoSwitch);
    switchView.on.get('dom-ready')?.();
    assert.ok(!switchView.calls.some((name) => name.startsWith('executeJavaScript')));
});

test('the window seals the page’s view with the link, the script, the gesture watch and the one opener, and our switch’s without them', () => {
    const source = readFileSync(join(root, 'src', 'demo-window.ts'), 'utf8');
    assert.match(source, /sheet: demoPageSheet\(deps\.platform\),/);
    assert.match(source, /script: demoLinkScript,/);
    assert.match(source, /asked: demoLinkOpener\(deps\.open, watchDemoGestures\(page\.webContents\)\),/);
    assert.equal(source.match(/demoLinkScript|demoLinkOpener|watchDemoGestures/g)?.length, 6, 'the import and the page’s view alone');
});

test('the address is spelled once, is the one the page’s note names, and the sheet and the script name no other', () => {
    assert.equal(demoRepository, 'https://github.com/reply-team/jason-ai');
    assert.equal(`https://${demoRepositoryText}`, demoRepository);
    // The page names it once, as the whole text of an element of its note; with no scheme, it adds nothing to its counted list.
    const page = pageText();
    assert.equal(page.split(demoRepositoryText).length - 1, 1);
    assert.ok(page.includes(`get the code at <b>${demoRepositoryText}</b>`));
    assert.ok(!page.includes(demoLinkMark), 'nothing of the page carries the mark');
    const source = readFileSync(join(root, 'src', 'demo-page.ts'), 'utf8');
    assert.equal(source.split('github.com/reply-team/jason-ai').length - 1, 2, 'once in the constant, once in its comment');
    const pattern = listed().pattern;
    assert.deepEqual([...addressesIn(demoLinkSheet, pattern).keys()], []);
    assert.deepEqual([...addressesIn(demoLinkScript, pattern).entries()], [['https://github.com', 1]]);
    assert.equal(demoLinkScript.split(JSON.stringify(demoRepository)).length - 1, 1);
    // The link takes the page's own link colour, a pointer, and an underline on hover and focus.
    assert.match(demoLinkSheet, /color: var\(--teal, #2f5be0\) !important/);
    assert.match(demoLinkSheet, /cursor: pointer !important/);
    assert.match(demoLinkSheet, /:hover, \[data-jason-link\]:focus-visible \{ text-decoration: underline !important;/);
    assert.ok(page.includes('.uinote a{color:var(--teal);'), 'the page’s own links in its note are of that colour');
});

// A small stand-in of the document the script runs in: elements, text, a tree walker, an observer, a location.
class FakeText {
    readonly nodeType = 3;
    parentElement: FakeElement | null = null;
    data: string;
    constructor(data: string) {
        this.data = data;
    }
    get textContent(): string {
        return this.data;
    }
}
class FakeElement {
    readonly nodeType = 1;
    parentElement: FakeElement | null = null;
    readonly attributes = new Map<string, string>();
    readonly children: (FakeElement | FakeText)[];
    constructor(...children: (FakeElement | FakeText)[]) {
        this.children = children;
        for (const child of children) child.parentElement = this;
    }
    get textContent(): string {
        return this.children.map((child) => child.textContent).join('');
    }
    hasAttribute(name: string): boolean {
        return this.attributes.has(name);
    }
    setAttribute(name: string, value: string): void {
        this.attributes.set(name, value);
    }
    removeAttribute(name: string): void {
        this.attributes.delete(name);
    }
    append(child: FakeElement | FakeText): void {
        this.children.push(child);
        child.parentElement = this;
    }
    closest(selector: string): FakeElement | null {
        const name = /^\[(.+)\]$/.exec(selector)?.[1];
        assert.ok(name, selector);
        for (let element: FakeElement | null = this; element !== null; element = element.parentElement) if (element.hasAttribute(name)) return element;
        return null;
    }
}

function texts(node: FakeElement | FakeText): FakeText[] {
    return node instanceof FakeText ? [] : node.children.flatMap((child) => (child instanceof FakeText ? [child] : texts(child)));
}

function runLinkScript(documentElement: FakeElement) {
    const listeners = new Map<string, (event: unknown) => void>();
    const observers: ((records: unknown[]) => void)[] = [];
    const navigations: string[] = [];
    const document = {
        documentElement,
        addEventListener: (type: string, listener: (event: unknown) => void, capture: boolean) => {
            assert.equal(capture, true);
            listeners.set(type, listener);
        },
        createTreeWalker: (root: FakeElement | FakeText) => {
            const found = texts(root);
            let at = 0;
            return {
                currentNode: root as FakeElement | FakeText,
                nextNode() {
                    const next = found[at++] ?? null;
                    if (next !== null) this.currentNode = next;
                    return next;
                },
            };
        },
    };
    runInNewContext(demoLinkScript, {
        document,
        Element: FakeElement,
        Node: { TEXT_NODE: 3 },
        NodeFilter: { SHOW_TEXT: 4 },
        MutationObserver: class {
            constructor(callback: (records: unknown[]) => void) {
                observers.push(callback);
            }
            observe() {}
        },
        location: {
            set href(url: string) {
                navigations.push(url);
            },
        },
    });
    const fire = (type: string, fields: Record<string, unknown>) => {
        let prevented = false;
        listeners.get(type)?.({ isTrusted: true, button: 0, repeat: false, ...fields, preventDefault: () => void (prevented = true) });
        return prevented;
    };
    return { navigations, fire, observe: (records: unknown[]) => observers.forEach((observer) => observer(records)) };
}

const isLink = (element: FakeElement) => element.hasAttribute(demoLinkMark) && element.attributes.get('role') === 'link' && element.attributes.get('tabindex') === '0';

test('the script marks the note’s address as a focusable link, asks nothing on load, and asks for the address on a person’s click or Enter alone', () => {
    const address = new FakeElement(new FakeText(demoRepositoryText));
    const demo = new FakeElement(new FakeText('Demo'));
    const longer = new FakeElement(new FakeText(`${demoRepositoryText}/releases`));
    const note = new FakeElement(new FakeElement(new FakeText('get the code at '), address), demo, longer);
    const run = runLinkScript(new FakeElement(new FakeElement(note)));
    assert.deepEqual(run.navigations, [], 'nothing is asked on load');
    assert.ok(isLink(address));
    assert.equal(demo.attributes.size + longer.attributes.size, 0, 'nothing else of the page is touched');

    assert.equal(run.fire('click', { target: demo }), false);
    assert.equal(run.fire('click', { target: address, isTrusted: false }), false, 'a click the page made is not the person’s');
    assert.equal(run.fire('click', { target: address, button: 1 }), false);
    assert.equal(run.fire('keydown', { target: address, key: ' ' }), false);
    assert.equal(run.fire('keydown', { target: address, key: 'Enter', repeat: true }), false);
    assert.equal(run.fire('keydown', { target: address, key: 'Enter', isTrusted: false }), false);
    assert.deepEqual(run.navigations, []);

    assert.equal(run.fire('click', { target: address }), true);
    assert.equal(run.fire('keydown', { target: address, key: 'Enter' }), true);
    assert.deepEqual(run.navigations, [demoRepository, demoRepository]);
});

test('the script keeps the mark as the page draws its note again, and takes it off an element whose text is no longer the address', () => {
    const body = new FakeElement();
    const run = runLinkScript(new FakeElement(body));
    const address = new FakeElement(new FakeText(demoRepositoryText));
    const note = new FakeElement(new FakeElement(new FakeText('get the code at '), address));
    body.append(note);
    run.observe([{ type: 'childList', addedNodes: [note] }]);
    assert.ok(isLink(address), 'a note drawn again is marked again');
    const words = address.children[0] as FakeText;
    words.data = 'something else';
    run.observe([{ type: 'characterData', target: words }]);
    assert.equal(address.attributes.size, 0, 'mark, role and focus taken off');
    assert.equal(run.fire('click', { target: address }), false);
    assert.deepEqual(run.navigations, []);
});
