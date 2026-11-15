/**
 * The seal around a view of the Demo: the page the application carries as one static file (`demo/index.html`), shown as
 * it was built, and the desktop's own Demo | Live switch drawn over it (`shell/demo-switch.html`). The page is not the
 * desktop's code, so its view is made harmless whatever its script does: no preload and so no bridge to the main process,
 * Chromium's sandbox, a session of its own that Electron keeps in memory, the one file it is served from and nothing
 * else, no pop-up, no navigation away, no download, every permission and every device chooser refused, and a content
 * policy sent as a header of the protocol that serves the file, before a byte of the page is read, so nothing in the
 * page can loosen it: a policy the page states itself is one more that also applies, never one that replaces it.
 *
 * Every piece of it is here, pure or wired onto the view's own contents and session (`sealView`), so a test runs the same
 * code over stand-ins and holds each piece; `demo-window.ts` makes the views and calls it before anything is loaded.
 * Nothing here imports Electron's runtime, only its types.
 */

import type { Rectangle, Session, WebContents, WebPreferences } from 'electron';

/** A view of the Demo: the scheme its one file is served by, that file's address, and its session's name. */
export interface DemoView {
    scheme: string;
    url: string;
    /** A partition named without the persistent prefix: Electron keeps the session in memory only, so nothing of it reaches the disk. */
    partition: string;
}

/** The page the application carries. */
export const demoPage: DemoView = { scheme: 'jason-demo', url: 'jason-demo://page/index.html', partition: 'jason-demo-page' };

/** The desktop's own switch, drawn over the page's own. */
export const demoSwitch: DemoView = { scheme: 'jason-switch', url: 'jason-switch://switch/index.html', partition: 'jason-demo-switch' };

/**
 * How the two schemes are registered before Electron is ready: standard, so a page has an origin of its own and its
 * storage works, and secure, as a page served from the application is. Nothing else: never `bypassCSP`, never the Fetch
 * API, CORS, service workers or streams.
 */
export function demoSchemes(): { scheme: string; privileges: { standard: boolean; secure: boolean } }[] {
    return [demoPage, demoSwitch].map((view) => ({ scheme: view.scheme, privileges: { standard: true, secure: true } }));
}

/** The view's preferences: no preload, so no bridge; the sandbox; no Node anywhere; no webview; no DevTools. */
export function demoViewPreferences(view: DemoView): WebPreferences {
    return {
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
    };
}

/**
 * The content policy, a header of the one response: the page's inline script and inline style, its fonts and images as
 * `data:` addresses, and nothing else: no address to fetch, connect or open a socket to, no frame, worker, plugin, form
 * target or base. Its `sandbox` keeps scripts and the page's own origin, so its storage works, and lets its forms raise
 * their events for its script, which `form-action` keeps from going anywhere; it leaves out pop-ups, top-level
 * navigation, downloads, modal dialogs and printing, pointer lock, presentation and orientation lock.
 */
export const demoPolicy = [
    "default-src 'none'",
    "script-src 'unsafe-inline'",
    "style-src 'unsafe-inline'",
    'font-src data:',
    'img-src data:',
    "connect-src 'none'",
    "media-src 'none'",
    "object-src 'none'",
    "frame-src 'none'",
    "child-src 'none'",
    "worker-src 'none'",
    "manifest-src 'none'",
    "form-action 'none'",
    "base-uri 'none'",
    "frame-ancestors 'none'",
    'sandbox allow-scripts allow-same-origin allow-forms',
].join('; ');

/** The browser features a page may ask for, each allowed to no origin, its own included. */
export const deniedFeatures = [
    'accelerometer',
    
    'autoplay',
    'bluetooth',
    'camera',
    'clipboard-read',
    'clipboard-write',
    'display-capture',
    'encrypted-media',
    'fullscreen',
    'gamepad',
    'geolocation',
    'gyroscope',
    'hid',
    'idle-detection',
    'keyboard-map',
    'local-fonts',
    'magnetometer',
    'microphone',
    'midi',
    'otp-credentials',
    'payment',
    'picture-in-picture',
    'publickey-credentials-create',
    'publickey-credentials-get',
    'screen-wake-lock',
    'serial',
    'storage-access',
    'usb',

    'window-management',
    'xr-spatial-tracking',
];

/** The headers the one response carries. */
export function demoHeaders(): Record<string, string> {
    return {
        'Content-Type': 'text/html; charset=utf-8',
        'Content-Security-Policy': demoPolicy,
        'Permissions-Policy': deniedFeatures.map((feature) => `${feature}=()`).join(', '),
        'X-Content-Type-Options': 'nosniff',
        'X-DNS-Prefetch-Control': 'off',
        'Referrer-Policy': 'no-referrer',
        'Cross-Origin-Opener-Policy': 'same-origin',
        'Cross-Origin-Resource-Policy': 'same-origin',
        'Cache-Control': 'no-store',
    };
}

/** What the view's scheme answers a request. */
export interface DemoAnswer {
    status: number;
    headers: Record<string, string>;
    body: Uint8Array | null;
}

/**
 * Pure: the scheme's answer. The view's one address, asked with GET, gets the file with the headers above; anything else
 * (another path, a query, a fragment, another method) gets nothing. The file is read only for the one address.
 */
export function answerDemo(view: DemoView, url: string, method: string, read: () => Uint8Array): DemoAnswer {
    if (method !== 'GET' || url !== view.url) return { status: 404, headers: { 'Cache-Control': 'no-store' }, body: null };
    return { status: 200, headers: demoHeaders(), body: read() };
}

/**
 * Pure: whether the view's session lets a request go on: its one address alone, and a `data:` address, which the policy
 * holds to fonts and images. Every other request is cancelled: http and https, a WebSocket, `file:` and any other scheme.
 */
export function requestAllowed(view: DemoView, url: string): boolean {
    return url === view.url || url.startsWith('data:');
}

/** Pure: whether a navigation goes on: only the main frame's to the view's one address, a reload. */
export function navigationAllowed(view: DemoView, url: string, isMainFrame: boolean): boolean {
    return isMainFrame && url === view.url;
}

/**
 * The proxy the view's session is given: one that is not there, on the loopback, with the loopback held to it as well.
 * Every request is cancelled before it would reach it; what goes on without a request (a host name looked up ahead, a
 * connection opened ahead, a WebRTC candidate, which `disable_non_proxied_udp` sends through it) meets a dead end.
 */
export const deadProxy = { mode: 'fixed_servers', proxyRules: 'http://127.0.0.1:1', proxyBypassRules: '<-loopback>' } as const;

/** What a sealed view refused, by kind only: never an address, never anything the page wrote. */
export type DemoRefusal = 'navigation' | 'popup' | 'request' | 'download' | 'permission' | 'device' | 'unload' | 'devtools' | 'webview' | 'login';

export interface SealDeps {
    /** The view's one file: read when it is asked for. */
    read(): Uint8Array;
    /** Told what was refused, each kind at most once (`onceEach`). */
    refused(kind: DemoRefusal): void;
    /** A navigation the view asked for, after it has been refused: the switch reads the person's choice from it. */
    asked?(url: string): void;
}

/** A refusal hook that passes each kind on once, so a page that asks again and again cannot fill the log. */
export function onceEach(tell: (kind: DemoRefusal) => void): (kind: DemoRefusal) => void {
    const told = new Set<DemoRefusal>();
    return (kind) => {
        if (told.has(kind)) return;
        told.add(kind);
        tell(kind);
    };
}

/** An event a handler may stop. */
interface Stoppable {
    preventDefault(): void;
}

/** The sessions sealed in this process: a partition's session lasts as long as the process, and is sealed once. */
const sealedSessions = new WeakSet<Session>();

/**
 * Seals a view's contents and its session before anything is loaded into it. Answers once the session's proxy is set,
 * which is what a load waits for. A session an earlier view of the same partition sealed is left as it is.
 */
export async function sealView(contents: WebContents, view: DemoView, deps: SealDeps): Promise<void> {
    const refuse = (kind: DemoRefusal) => deps.refused(kind);
    sealContents(contents, view, deps, refuse);
    const session: Session = contents.session;
    if (sealedSessions.has(session)) return;
    sealedSessions.add(session);

    // The one file, by the view's own scheme, with the policy as a header.
    session.protocol.handle(view.scheme, (request) => {
        const answer = answerDemo(view, request.url, request.method, deps.read);
        if (answer.status !== 200) refuse('request');
        return new Response(answer.body === null ? null : Buffer.from(answer.body), { status: answer.status, headers: answer.headers });
    });
    // The schemes a page reaches the world or the disk by answer nothing in this session, whatever else let them through.
    for (const scheme of ['http', 'https', 'file']) {
        session.protocol.handle(scheme, () => {
            refuse('request');
            return new Response(null, { status: 403 });
        });
    }
    session.webRequest.onBeforeRequest((details, callback) => {
        const allowed = requestAllowed(view, details.url);
        if (!allowed) refuse('request');
        callback({ cancel: !allowed });
    });

    session.setPermissionRequestHandler((_contents, _permission, callback) => {
        refuse('permission');
        callback(false);
    });
    session.setPermissionCheckHandler(() => false);
    session.setDevicePermissionHandler(() => false);
    session.setDisplayMediaRequestHandler((_request, callback) => {
        refuse('permission');
        callback(null);
    });
    session.setBluetoothPairingHandler((_details, callback) => {
        refuse('device');
        callback({ confirmed: false });
    });
    session.on('select-hid-device', (event: Stoppable, _details: unknown, callback: (deviceId?: string | null) => void) => {
        event.preventDefault();
        refuse('device');
        callback();
    });
    session.on('select-serial-port', (event: Stoppable, _ports: unknown, _contents: unknown, callback: (portId: string) => void) => {
        event.preventDefault();
        refuse('device');
        callback('');
    });
    session.on('select-usb-device', (event: Stoppable, _details: unknown, callback: (deviceId?: string) => void) => {
        event.preventDefault();
        refuse('device');
        callback();
    });
    session.on('will-download', (event: Stoppable, item: { cancel(): void }) => {
        event.preventDefault();
        item.cancel();
        refuse('download');
    });
    session.setSpellCheckerEnabled(false);
    await session.setProxy({ ...deadProxy });
}

/** The view's own contents: navigation, pop-ups, webviews, unload, DevTools, sign-in boxes, certificates, Bluetooth, WebRTC. */
function sealContents(contents: WebContents, view: DemoView, deps: SealDeps, refuse: (kind: DemoRefusal) => void): void {
    const guard = (event: Stoppable, url: string, isMainFrame: boolean) => {
        if (navigationAllowed(view, url, isMainFrame)) return;
        event.preventDefault();
        refuse('navigation');
        deps.asked?.(url);
    };
    contents.on('will-navigate', (event) => guard(event, event.url, event.isMainFrame));
    contents.on('will-frame-navigate', (event) => guard(event, event.url, event.isMainFrame));
    contents.on('will-redirect', (event) => guard(event, event.url, event.isMainFrame));
    contents.setWindowOpenHandler(() => {
        refuse('popup');
        return { action: 'deny' };
    });
    contents.on('will-attach-webview', (event) => {
        event.preventDefault();
        refuse('webview');
    });
    // A page's beforeunload never holds the window open, nor the quit.
    contents.on('will-prevent-unload', (event) => {
        event.preventDefault();
        refuse('unload');
    });
    contents.on('devtools-opened', () => {
        contents.closeDevTools();
        refuse('devtools');
    });
    contents.on('login', (event, _details, _authInfo, callback) => {
        event.preventDefault();
        refuse('login');
        callback();
    });
    contents.on('certificate-error', (event, _url, _error, _certificate, callback) => {
        event.preventDefault();
        callback(false);
    });
    contents.on('select-bluetooth-device', (event, _devices, callback) => {
        event.preventDefault();
        refuse('device');
        callback('');
    });
    contents.setWebRTCIPHandlingPolicy('disable_non_proxied_udp');
}

/** The person's choice, from a navigation the switch asked for: `jason-switch://switch/live` is Live; nothing else is one. */
export function switchChoice(url: string): 'live' | null {
    return url === 'jason-switch://switch/live' ? 'live' : null;
}

/**
 * Where our switch stands: the right end of the page's top strip, where the page draws its own, whatever the width.
 * Measured from the page as the sealed view draws it at 1440, 1100 and 900 wide: the strip is 37 pixels high above a
 * one-pixel border, and what the page draws at its right end is 210 wide with 12 to spare on the right, at every width.
 * Ours covers that end and 4 pixels more, the strip's full height, and leaves the border to the page.
 */
export const switchSize = { width: 226, height: 37 } as const;

/** Pure: our switch's bounds in a window's content area of this width. */
export function switchBounds(contentWidth: number): Rectangle {
    const width = Math.max(0, Math.min(switchSize.width, contentWidth));
    return { x: Math.max(0, contentWidth - width), y: 0, width, height: switchSize.height };
}
