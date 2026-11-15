// The Demo is a static page shown as it was built, so its view is held harmless whatever its script does: no preload,
// the sandbox, a session of its own in memory, the one file it is served from, no pop-up, no navigation away, no
// download, every permission and device refused, and the content policy as a header. Each piece runs here over a
// stand-in for Electron's contents and session that records what was installed and lets the test act as the page.

import assert from 'node:assert/strict';
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
    demoMacFrame,
    switchChoice,
    type DemoRefusal,
} from '../src/demo-page.ts';
import { persists } from '../src/window-pairing.ts';

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
        "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; font-src data:; img-src data:; connect-src 'none'; media-src 'none'; object-src 'none'; frame-src 'none'; child-src 'none'; worker-src 'none'; manifest-src 'none'; form-action 'none'; base-uri 'none'; frame-ancestors 'none'; sandbox allow-scripts allow-same-origin allow-forms",
    );
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

test('the scheme serves the one address to GET alone; another path, a query, a fragment or a method gets nothing, and the file is not read', () => {
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
        ['jason-demo://page/index.html#x', 'GET'],
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
    await sealView(made.contents, view, { read: () => pageBytes, refused: (kind) => refused.push(kind), asked: (url) => asked.push(url) });
    return { ...made, refused, asked };
}

const call = (handler: Handler | undefined, ...args: unknown[]): unknown => {
    assert.ok(handler, 'the handler was installed');
    return (handler as (...args: unknown[]) => unknown)(...args);
};

test('every handler of the seal is installed on the contents and the session, the proxy last', async () => {
    const view = await sealed();
    assert.deepEqual([...view.on.keys()].sort(), ['certificate-error', 'devtools-opened', 'login', 'select-bluetooth-device', 'will-attach-webview', 'will-frame-navigate', 'will-navigate', 'will-prevent-unload', 'will-redirect']);
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
});

// ---- Our switch's place and page --------------------------------------------------------------------------------------

test('the page’s strip is fixed: right of its 284-pixel sidebar, 38 high; on macOS a band of 26 lies above the page', () => {
    assert.deepEqual(demoGeometry, { sidebar: 284, strip: 38, macBand: 26 });
});

test('elsewhere than macOS the page fills the content and our switch covers the strip, at every width the window allows', () => {
    for (const platform of ['win32', 'linux'] as const) {
        assert.deepEqual(demoLayout(platform, 1440, 900), { page: { x: 0, y: 0, width: 1440, height: 900 }, switch: { x: 284, y: 0, width: 1156, height: 38 }, band: null });
        assert.deepEqual(demoLayout(platform, 900, 600), { page: { x: 0, y: 0, width: 900, height: 600 }, switch: { x: 284, y: 0, width: 616, height: 38 }, band: null });
    }
    // Narrower than the sidebar, which the window's least width never is, the switch has no width rather than a negative one.
    assert.deepEqual(demoLayout('linux', 200, 100).switch, { x: 284, y: 0, width: 0, height: 38 });
});

test('on macOS the page and our switch start 26 below the window’s top, under our band, which is the whole width', () => {
    assert.deepEqual(demoLayout('darwin', 1440, 900), {
        page: { x: 0, y: 26, width: 1440, height: 874 },
        switch: { x: 284, y: 26, width: 1156, height: 38 },
        band: { x: 0, y: 0, width: 1440, height: 26 },
    });
    assert.equal(demoLayout('darwin', 900, 10).page.height, 0, 'never a negative height');
});

test('our switch’s page holds no script, links only to the choice of Live, and is the window’s drag region but for the switch', () => {
    const page = readFileSync(join(root, 'shell', 'demo-switch.html'), 'utf8');
    assert.ok(!/<script|on[a-z]+\s*=|javascript:/i.test(page), 'no script, no handler');
    assert.deepEqual([...page.matchAll(/href="([^"]*)"/g)].map((match) => match[1]), ['jason-switch://switch/live']);
    assert.ok(!/src=|url\(|@import/i.test(page), 'it loads nothing');
    const css = page.replace(/\s+/g, ' ');
    assert.ok(/html, body \{[^}]*-webkit-app-region: drag;/.test(css), 'the strip drags the window');
    assert.ok(/\.switch \{[^}]*-webkit-app-region: no-drag;/.test(css), 'the switch takes the click');
    assert.ok(/body \{[^}]*background: #fff4d6;[^}]*border-bottom: 1px solid #f2dc9c;/.test(css), 'the Demo strip’s colours');
});

test('our band on macOS holds no script and nothing to click, is the sidebar’s colour over 284 and the strip’s beyond, and drags', () => {
    const page = readFileSync(join(root, 'shell', 'demo-band.html'), 'utf8');
    assert.ok(!/<script|on[a-z]+\s*=|javascript:|href=|<a\b|<button|<input/i.test(page), 'no script and nothing to follow');
    assert.ok(!/src=|url\(|@import/i.test(page), 'it loads nothing');
    const css = page.replace(/\s+/g, ' ');
    assert.ok(/html \{[^}]*-webkit-app-region: drag;/.test(css));
    assert.ok(/background: linear-gradient\(to right, #f3f5f9 0 284px, #fff4d6 284px\);/.test(css));
    assert.ok(!/no-drag/.test(css), 'all of it drags');
});

test('the Demo window on macOS hides its title bar into our band, loads the band under its views, and elsewhere keeps its frame', () => {
    const main = readFileSync(join(root, 'src', 'main.ts'), 'utf8');
    const demo = main.slice(main.indexOf('async function runDemo('), main.indexOf('function keepElectronUnderData('));
    assert.ok(demo.includes("...(process.platform === 'darwin' ? demoMacFrame : {}),"));
    const window = readFileSync(join(root, 'src', 'demo-window.ts'), 'utf8');
    assert.ok(window.includes("if (deps.platform === 'darwin') await window.webContents.loadFile(bandFile);"));
    assert.ok(window.includes('const at = demoLayout(deps.platform, width, height);'));
    assert.deepEqual(demoMacFrame.titleBarStyle, 'hidden');
});

// ---- What the Demo reaches --------------------------------------------------------------------------------------------

/** The modules a source file imports, by their names under src/. */
function importsOf(file: string): string[] {
    const text = readFileSync(join(root, 'src', file), 'utf8');
    return [...text.matchAll(/^import\s[^;]*?from\s+'([^']+)'/gm)].map((match) => match[1] ?? '');
}

test('the Demo’s two modules reach no jason, no runtime, no program, no process and nothing of the earlier demo', () => {
    const refused = /^\.\/(cli|programs|installation|own-runtime|chat.*|setup|repair|window-pairing|browser-bridge|ipc|preload|demo-(seed|archive|over|runtime|start|settings|plugin|folders|alive|story))\.ts$|child_process|^node:net$|^node:http/;
    for (const file of ['demo-page.ts', 'demo-window.ts']) {
        const imports = importsOf(file);
        assert.ok(imports.length > 0, file);
        for (const name of imports) assert.ok(!refused.test(name), `${file} imports ${name}`);
    }
    assert.deepEqual(importsOf('demo-page.ts'), ['electron']);
    assert.deepEqual(importsOf('demo-window.ts'), ['electron', 'node:fs', 'node:path', './demo-page.ts']);
    // demo-page.ts takes Electron's types alone, so its code runs here without Electron.
    assert.match(readFileSync(join(root, 'src', 'demo-page.ts'), 'utf8'), /^import type \{[^}]*\} from 'electron';$/m);
});
