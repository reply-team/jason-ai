/**
 * The seal around a view of the Demo: the page the application carries as one static file (`demo/index.html`), shown as
 * it was built, and the desktop's own Demo | Live switch drawn over the place its strip leaves for one
 * (`shell/demo-switch.html`). The page is not the desktop's code, so its view is made harmless whatever its script does:
 * no preload and so no bridge to the main process, Chromium's sandbox, a session of its own that Electron keeps in
 * memory, the one file it is served from and nothing else, no pop-up, no navigation away, no download, every permission
 * and every device chooser refused, and a content policy sent as a header of the protocol that serves the file, before a
 * byte of the page is read, so nothing in the page can loosen it: a policy the page states itself is one more that also
 * applies, never one that replaces it. The policy's `sandbox` is the page's own frame, scripts alone, so its origin is
 * opaque.
 *
 * Every piece of it is here, pure or wired onto the view's own contents and session (`sealView`), so a test runs the same
 * code over stand-ins and holds each piece; `demo-window.ts` makes the views and calls it before anything is loaded.
 * Nothing here imports Electron's runtime, only its types.
 */

import type { Rectangle, Session, WebContents, WebPreferences } from 'electron';
import { stripHeight, titleBand } from './window-frame.ts';

/** A view of the Demo: the scheme its one file is served by, that file's address, and its session's name. */
export interface DemoView {
    scheme: string;
    url: string;
    /** A partition named without the persistent prefix: Electron keeps the session in memory only, so nothing of it reaches the disk. */
    partition: string;
    /** Whether the regions the view's page marks as the window's drag region stay so: ours alone; the page's never do. */
    drags: boolean;
}

/** The page the application carries. */
export const demoPage: DemoView = { scheme: 'jason-demo', url: 'jason-demo://page/index.html', partition: 'jason-demo-page', drags: false };

/** The desktop's own switch, drawn over the page's own. */
export const demoSwitch: DemoView = { scheme: 'jason-switch', url: 'jason-switch://switch/index.html', partition: 'jason-demo-switch', drags: true };

/**
 * The Chromium switch a Demo start appends before Electron is ready: every host name maps to nothing, so this run resolves
 * none, through the system's resolver or any other. A page reaches no address through the content policy and the
 * session's guards, but a host name it names ahead (`<link rel="dns-prefetch">`) is looked up below all of them; with this
 * rule nothing is. The Demo needs no name: it runs no runtime, offers no update and loads nothing but the application's
 * own files. A Live start is a start of its own, made with its own arguments (`relaunchOptions`), and never has it.
 */
export const demoResolverRule = ['host-resolver-rules', 'MAP * ~NOTFOUND'] as const;

/** What of Electron a Demo start uses before it is ready: its command line and its protocol registry. */
export interface BeforeReady {
    commandLine: { appendSwitch(name: string, value?: string): void };
    protocol: { registerSchemesAsPrivileged(schemes: ReturnType<typeof demoSchemes>): void };
}

/** A Demo start, before Electron is ready: no host name resolved, and the Demo's two schemes known. */
export function demoBeforeReady(commandLine: BeforeReady['commandLine'], protocol: BeforeReady['protocol']): void {
    commandLine.appendSwitch(...demoResolverRule);
    protocol.registerSchemesAsPrivileged(demoSchemes());
}

/**
 * The sheet put into each document of a view whose page's drag regions are not kept: of the user's origin, every
 * element and pseudo-element no-drag and important, which no rule of a page outranks, an inline one included. In a
 * window without a frame (macOS) a region a lower view marks as drag takes the clicks of a view drawn over it, so the
 * page could otherwise take the clicks meant for our switch; the window is moved by our band and our strip.
 */
export const noDragSheet = '*, *::before, *::after { -webkit-app-region: no-drag !important; }';

/**
 * How the two schemes are registered before Electron is ready: standard, so an address has a host and a path, and secure,
 * as a page served from the application is. Nothing else: never `bypassCSP`, never the Fetch API, CORS, service workers
 * or streams. The page's origin is opaque all the same: its policy's `sandbox` leaves out same-origin.
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
 * target or base. Its `sandbox` is the page's own frame, the one it was built and walked in: scripts and nothing more.
 * Without same-origin the page's origin is opaque, so its storage is refused and it keeps what it keeps in memory; it
 * leaves out forms, pop-ups, top-level navigation, downloads, modal dialogs and printing, pointer lock, presentation and
 * orientation lock. The desktop's own refusals below hold the page to the same whatever the policy does.
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
    'sandbox allow-scripts',
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
 * Pure: whether an address is the view's own: the same text up to its fragment, which names a place in the page and asks
 * nothing more of the scheme, so a page that moved itself to a fragment is still its own page when reloaded. A fragment
 * begins at the first `#` of an address, so the scheme, the host, the path and the query are compared whole.
 */
export function isOwnAddress(view: DemoView, url: string): boolean {
    const hash = url.indexOf('#');
    return (hash < 0 ? url : url.slice(0, hash)) === view.url;
}

/**
 * Pure: the scheme's answer. The view's one address, asked with GET at any fragment, gets the file with the headers above;
 * anything else (another path, a query, another method) gets nothing. The file is read only for the one address.
 */
export function answerDemo(view: DemoView, url: string, method: string, read: () => Uint8Array): DemoAnswer {
    if (method !== 'GET' || !isOwnAddress(view, url)) return { status: 404, headers: { 'Cache-Control': 'no-store' }, body: null };
    return { status: 200, headers: demoHeaders(), body: read() };
}

/**
 * Pure: whether the view's session lets a request go on: its one address alone, at any fragment, and a `data:` address,
 * which the policy holds to fonts and images. Every other request is cancelled: http and https, a WebSocket, `file:` and
 * any other scheme.
 */
export function requestAllowed(view: DemoView, url: string): boolean {
    return isOwnAddress(view, url) || url.startsWith('data:');
}

/** Pure: whether a navigation goes on: only the main frame's to the view's one address, at any fragment, a reload. */
export function navigationAllowed(view: DemoView, url: string, isMainFrame: boolean): boolean {
    return isMainFrame && isOwnAddress(view, url);
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
    /**
     * A navigation the view asked for, after it has been stopped: the switch reads the person's choice from it, and
     * answers true when it was one, which is then not told as a refusal.
     */
    asked?(url: string): boolean;
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

/**
 * The view's own contents: navigation, pop-ups, webviews, unload, DevTools, sign-in boxes, certificates, Bluetooth, WebRTC,
 * and the page's drag regions.
 */
function sealContents(contents: WebContents, view: DemoView, deps: SealDeps, refuse: (kind: DemoRefusal) => void): void {
    const guard = (event: Stoppable, url: string, isMainFrame: boolean) => {
        if (navigationAllowed(view, url, isMainFrame)) return;
        event.preventDefault();
        if (deps.asked?.(url) === true) return;
        refuse('navigation');
    };
    contents.on('will-navigate', (event) => guard(event, event.url, event.isMainFrame));
    contents.on('will-frame-navigate', (event) => guard(event, event.url, event.isMainFrame));
    contents.on('will-redirect', (event) => guard(event, event.url, event.isMainFrame));
    // A navigation Electron asks no handler about, `about:blank` from the page's script, is undone once it is made: the
    // main frame shows its own address or is loaded with it again.
    contents.on('did-navigate', (_event, url) => {
        if (isOwnAddress(view, url)) return;
        refuse('navigation');
        if (!contents.isDestroyed()) contents.loadURL(view.url).catch(() => undefined);
    });
    if (!view.drags) {
        contents.on('dom-ready', () => {
            // A document gone before the sheet lands refuses it; the next document gets its own.
            contents.insertCSS(noDragSheet, { cssOrigin: 'user' }).catch(() => undefined);
        });
    }
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
 * Where the Demo's views stand. The page's own numbers, which are fixed: its sidebar is 284 pixels wide, and right of it
 * its top strip is 38 high (the strip and its one-pixel border), with its own dot and words at its left and its right end
 * left free for a switch. Our view lies over that strip whole, from the sidebar to the window's right edge, the window's
 * drag region; it is transparent, so the page's words show through, and draws the switch alone at the right end. On macOS the window's title bar is hidden,
 * so the traffic lights stand in the window's top 26 pixels; the page cannot know it is on a Mac and keeps nothing clear
 * for them, so there the page and our switch start 26 below the top, under a band of ours (`shell/demo-band.html`). On
 * Windows the caption buttons stand over the strip's right end, as high as it, and our switch's page leaves their width
 * free, told it in its own view's coordinates.
 */
export const demoGeometry = { sidebar: 284, strip: stripHeight, macBand: titleBand } as const;

export interface DemoLayout {
    page: Rectangle;
    switch: Rectangle;
    /** Our band above the page, on macOS; `null` elsewhere. */
    band: Rectangle | null;
}

/** Pure: the Demo's views in a window's content area of this size, on this platform. */
export function demoLayout(platform: NodeJS.Platform, width: number, height: number): DemoLayout {
    const top = platform === 'darwin' ? demoGeometry.macBand : 0;
    return {
        page: { x: 0, y: top, width, height: Math.max(0, height - top) },
        switch: { x: demoGeometry.sidebar, y: top, width: Math.max(0, width - demoGeometry.sidebar), height: demoGeometry.strip },
        band: top === 0 ? null : { x: 0, y: 0, width, height: top },
    };
}
