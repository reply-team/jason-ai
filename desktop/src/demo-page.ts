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
 * Without same-origin the page's origin is opaque (`null`): it shares nothing with any other page and sets no cookie, and
 * what it keeps lives in the view's session, which is in memory only. It leaves out forms, pop-ups, top-level
 * navigation, downloads, modal dialogs and printing, pointer lock, presentation and orientation lock. The desktop's own refusals below hold the page to the same whatever the policy does.
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
    /** A sheet of the desktop's own, of the user's origin, put into every document of the view (`demoStripSheets`). */
    sheet?: string | undefined;
    /** A script of the desktop's own, run in every document of the view in a world of its own (`demoLinkWorld`), never the page's. */
    script?: string | undefined;
    /**
     * A navigation of the view's main frame, asked for by the view and stopped: the switch reads the person's choice from
     * it, and answers true when it was one, which is then not told as a refusal. A frame's navigation within the page and
     * a redirect are refused and asked nothing.
     */
    asked?(url: string): boolean;
    /**
     * Told once for each document of the view, when every sheet put into it has settled, landed or refused: the page's
     * view is shown only then (`demoPageShowing`), so no drag region of a document is the window's before its sheet.
     */
    landed?(): void;
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
    const guard = (event: Stoppable, url: string, isMainFrame: boolean, asks: boolean) => {
        if (navigationAllowed(view, url, isMainFrame)) return;
        event.preventDefault();
        if (asks && isMainFrame && deps.asked?.(url) === true) return;
        refuse('navigation');
    };
    contents.on('will-navigate', (event) => guard(event, event.url, event.isMainFrame, true));
    contents.on('will-frame-navigate', (event) => guard(event, event.url, event.isMainFrame, true));
    contents.on('will-redirect', (event) => guard(event, event.url, event.isMainFrame, false));
    // A navigation Electron asks no handler about, `about:blank` from the page's script, is undone once it is made: the
    // main frame shows its own address or is loaded with it again.
    contents.on('did-navigate', (_event, url) => {
        if (isOwnAddress(view, url)) return;
        refuse('navigation');
        if (!contents.isDestroyed()) contents.loadURL(view.url).catch(() => undefined);
    });
    const sheets = [...(view.drags ? [] : [noDragSheet]), ...(deps.sheet === undefined ? [] : [deps.sheet])];
    const script = deps.script;
    const landed = deps.landed;
    if (sheets.length > 0 || script !== undefined || landed !== undefined) {
        contents.on('dom-ready', () => {
            // A document gone before a sheet or the script lands refuses it; the next document gets its own.
            const inserted = sheets.map((sheet) => contents.insertCSS(sheet, { cssOrigin: 'user' }).catch(() => undefined));
            if (script !== undefined) contents.executeJavaScriptInIsolatedWorld(demoLinkWorld, [{ code: script }]).catch(() => undefined);
            // Settled, not landed: a sheet refused is told too, and the bound of `demoPageShowing` covers one that never settles.
            if (landed !== undefined) void Promise.all(inserted).then(() => landed());
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
 * The one address the Demo leads out to: the repository the page's note names ("get the code at
 * github.com/reply-team/jason-ai"), as text. In the desktop's window that text is drawn as a link, and using it has the
 * desktop open this address in the person's browser. The page itself stays as it was built and reaches nothing: the
 * link is the desktop's own sheet and script, put into the page's view (`demoLinkSheet`, `demoLinkScript`), and the
 * address leaves the view only through the view's own navigation guard, which refuses the navigation as it refuses every
 * other and hands this address, compared whole, to the browser (`demoLinkOpener`). In a browser tab the page is the same
 * file and the address stays text.
 */
export const demoRepository = 'https://github.com/reply-team/jason-ai';

/** The words the note shows the address in: the address without its scheme. */
export const demoRepositoryText = demoRepository.slice('https://'.length);

/** The attribute the desktop's script marks the note's address with; nothing of the page carries it. */
export const demoLinkMark = 'data-jason-link';

/** The world the desktop's script runs in: one of its own beside the page's, sharing the document and nothing else. */
export const demoLinkWorld = 1001;

/** How long after the address was opened it is not opened again, so a page that asks for it again and again opens one tab. */
export const demoLinkSpanMs = 2000;

/**
 * The link's look, of the user's origin and important: the colour of the page's own links (`--teal`), a pointer, and an
 * underline on hover and on keyboard focus, as the page's own links in its note have.
 */
export const demoLinkSheet =
    `[${demoLinkMark}] { color: var(--teal, #2f5be0) !important; cursor: pointer !important; text-decoration: none !important; } ` +
    `[${demoLinkMark}]:hover, [${demoLinkMark}]:focus-visible { text-decoration: underline !important; text-underline-offset: 3px !important; }`;

/**
 * The script, run in every document of the page's view in a world of its own (`demoLinkWorld`). It marks the element
 * whose whole text is the address, and keeps marking it as the page draws its note again; the mark makes it focusable,
 * with the link role. A person's click on it, or Enter on it, asks for the address as a navigation of the view, which the
 * view's guard refuses and reads (`demoLinkOpener`); nothing else of the page is touched, and nothing is asked on load.
 */
export const demoLinkScript = `(() => {
    const text = ${JSON.stringify(demoRepositoryText)};
    const address = ${JSON.stringify(demoRepository)};
    const mark = ${JSON.stringify(demoLinkMark)};
    const unmark = (element) => {
        element.removeAttribute(mark);
        element.removeAttribute('role');
        element.removeAttribute('tabindex');
    };
    const markIn = (root) => {
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        for (let node = walker.currentNode; node !== null; node = walker.nextNode()) {
            if (node.nodeType !== Node.TEXT_NODE) continue;
            const element = node.parentElement;
            if (element === null) continue;
            const whole = element.textContent === text;
            if (whole && !element.hasAttribute(mark)) {
                element.setAttribute(mark, '');
                element.setAttribute('role', 'link');
                element.setAttribute('tabindex', '0');
            } else if (!whole && element.hasAttribute(mark)) unmark(element);
        }
    };
    new MutationObserver((records) => {
        for (const record of records) {
            if (record.type === 'characterData') markIn(record.target);
            else for (const node of record.addedNodes) markIn(node);
        }
    }).observe(document, { childList: true, subtree: true, characterData: true });
    const marked = (target) => {
        const element = target instanceof Element ? target.closest('[' + mark + ']') : null;
        return element !== null && element.textContent === text;
    };
    document.addEventListener('click', (event) => {
        if (!event.isTrusted || event.button !== 0 || !marked(event.target)) return;
        event.preventDefault();
        location.href = address;
    }, true);
    document.addEventListener('keydown', (event) => {
        if (!event.isTrusted || event.key !== 'Enter' || event.repeat || !marked(event.target)) return;
        event.preventDefault();
        location.href = address;
    }, true);
    markIn(document.documentElement);
})();`;

/**
 * How long after a person's own click or Enter in the page's view a navigation to the repository's address may open it.
 * The main process cannot see what the page's script does with a gesture; it sees the input that reached the view, so a
 * page that asks for the address on load, or again and again, opens nothing.
 */
export const demoLinkGestureMs = 1000;

/** The gesture watch's and the opener's clock: monotonic, so a step of the system's clock neither opens nor closes the second. */
const monotonic = (): number => performance.now();

/** What the gesture watch reads of an input event that reached the view: its type, and a key's name or a mouse button. */
export interface DemoInput {
    type: string;
    key?: string;
    button?: string;
    isAutoRepeat?: boolean;
}

/** Pure: whether an input event is a person's act on a link: the left mouse button let go, or Enter pressed (not held). */
export function isDemoLinkGesture(input: DemoInput): boolean {
    if (input.type === 'mouseUp') return input.button === 'left';
    return input.type === 'keyDown' && input.key === 'Enter' && input.isAutoRepeat !== true;
}

/**
 * Watches the input that reaches the page's view, a mouse button's by `input-event` and a key's by `before-input-event`,
 * and answers when the last person's act on a link (`isDemoLinkGesture`) reached it, or null before any.
 */
export function watchDemoGestures(contents: Pick<WebContents, 'on'>, now: () => number = monotonic): () => number | null {
    let last: number | null = null;
    const seen = (input: DemoInput) => {
        if (isDemoLinkGesture(input)) last = now();
    };
    contents.on('input-event', (_event, input) => seen(input as DemoInput));
    contents.on('before-input-event', (_event, input) => seen({ type: input.type, key: input.key, isAutoRepeat: input.isAutoRepeat }));
    return () => last;
}

/**
 * The page view's reader of a refused navigation of its main frame: a navigation to the repository's address, compared
 * as text whole, within `demoLinkGestureMs` of a person's click or Enter in the view (`gestureAt`), calls `open`, which
 * opens that address and is handed none, at most once in `demoLinkSpanMs`, and is answered as the person's choice rather
 * than a refusal. The address with no such act before it, and any other address, are refused and open nothing. Electron
 * asks about one navigation of the main frame twice (`will-navigate`, `will-frame-navigate`); the span opens it once.
 */
export function demoLinkOpener(open: () => void, gestureAt: () => number | null, now: () => number = monotonic): (url: string) => boolean {
    let last: number | null = null;
    return (url) => {
        if (url !== demoRepository) return false;
        const at = now();
        const gesture = gestureAt();
        // A moment before the gesture is a clock gone wrong, not a moment within its second.
        if (gesture === null || at < gesture || at - gesture > demoLinkGestureMs) return false;
        if (last !== null && at - last < demoLinkSpanMs) return true;
        last = at;
        open();
        return true;
    };
}

/**
 * The longest the page's view stays out of the window after a load of it began, waiting for its document's sheets to
 * settle (`SealDeps.landed`): past it the view is added whatever happened, a sheet that never settles, a document that
 * never comes, a load that fails, so the window never stays without its page.
 */
export const demoPageBoundMs = 1500;

/** What `demoPageShowing` needs of the window: the view added under ours, taken off, and a timer. */
export interface PageShowingDeps {
    /** Adds the page's view to the window under every view already there (index 0), so ours stays drawn over it. */
    add(): void;
    /** Takes the page's view off the window. */
    remove(): void;
    setTimer(run: () => void, ms: number): unknown;
    clearTimer(timer: unknown): void;
}

/** When the page's view is in the window: told of each load, of each document's sheets settled, and of the close. */
export interface PageShowing {
    /** A load of the page's document begins, the first or a reload: the view leaves the window until it is shown. */
    loading(): void;
    /** The document's sheets have settled: the view is shown, if it is not already. */
    landed(): void;
    /** The window closed: no timer is left, and nothing is shown again. */
    close(): void;
}

/**
 * The page's view is in the window only once the sheets of the document it holds have settled, or `demoPageBoundMs`
 * after the load began, whichever comes first. The page's own script makes its strip a drag region as it runs, before
 * the document's sheets can be put in; a view that is not in the window answers no hit test, so until the sheet that
 * makes no drag region of the page the window's has settled, no point of the page drags the window, and our switch never
 * answers as the caption. The same on every system: nothing here reads the platform.
 */
export function demoPageShowing(deps: PageShowingDeps): PageShowing {
    let shown = false;
    let closed = false;
    let timer: unknown = null;
    const stopTimer = () => {
        if (timer === null) return;
        deps.clearTimer(timer);
        timer = null;
    };
    const show = () => {
        stopTimer();
        if (closed || shown) return;
        shown = true;
        deps.add();
    };
    return {
        loading() {
            if (closed) return;
            stopTimer();
            if (shown) {
                shown = false;
                deps.remove();
            }
            timer = deps.setTimer(show, demoPageBoundMs);
        },
        landed: show,
        close() {
            closed = true;
            stopTimer();
        },
    };
}

/**
 * Where the Demo's views stand. The page's own numbers, which are fixed: its sidebar is 284 pixels wide, and right of
 * it its top strip is 38 high (the strip and its one-pixel border), with its own dot and words at its left and its
 * right end left free for a switch. Our view lies over that strip whole, from the sidebar to the window's right edge,
 * the window's drag region; it is transparent, so the page's words show through, and draws the switch alone at the
 * right end. The page and our switch start at the content's top edge on every system.
 *
 * On macOS the window's title bar is hidden, so the traffic lights stand in the window's top 26 pixels (`macBand`),
 * over the left end of the page's sidebar. The page cannot know it is on a Mac and keeps nothing clear for them, so
 * there the desktop puts a sheet of its own into the page's view (`demoMacSheet`) that moves the sidebar's contents
 * down by the band's height, and the window is dragged by a band of ours under the views (`shell/demo-band.html`): the
 * window's own contents, whose body is the drag region over the window's top 26 pixels, from the window's left edge to
 * the span at its right end where our switch stands (`macCut`), over the sidebar and over the Demo strip up to that
 * span. The span holds the switch by construction: the switch's page fixes the control's greatest extent in its own
 * stylesheet (`demoSwitchMost`: its box's greatest width, which its segments shrink to fit, its offset from the right
 * edge, its shadow), so no font draws it wider, and the span is those three together. Inside the span the switch's page
 * decides what drags and what takes a click, as on Windows and Linux: everything of it drags but the switch itself. On
 * Windows and Linux the caption buttons stand over the strip's right end, as high as it, and our switch's page leaves
 * their width free, told it in its own view's coordinates.
 */
export const demoSwitchMost = { width: 160, offset: 12, shadow: 5 } as const;

export const demoGeometry = {
    sidebar: 284,
    strip: stripHeight,
    macBand: titleBand,
    macCut: demoSwitchMost.width + demoSwitchMost.offset + demoSwitchMost.shadow,
} as const;

export interface DemoLayout {
    page: Rectangle;
    switch: Rectangle;
    /** Our band on macOS, under the views: the window's drag region over its top `macBand` pixels; `null` elsewhere. */
    band: Rectangle | null;
    /** The band's part that does not drag: its right `macCut` pixels, where our switch stands; `null` with no band. */
    cut: Rectangle | null;
}

/** Pure: the Demo's views in a window's content area of this size, on this platform. */
export function demoLayout(platform: NodeJS.Platform, width: number, height: number): DemoLayout {
    const mac = platform === 'darwin';
    const cut = Math.min(width, demoGeometry.macCut);
    return {
        page: { x: 0, y: 0, width, height },
        switch: { x: demoGeometry.sidebar, y: 0, width: Math.max(0, width - demoGeometry.sidebar), height: demoGeometry.strip },
        band: mac ? { x: 0, y: 0, width, height: demoGeometry.macBand } : null,
        cut: mac ? { x: width - cut, y: 0, width: cut, height: demoGeometry.macBand } : null,
    };
}

/**
 * The page's sheet on macOS, of the user's origin and important, as every sheet the desktop puts into the page's view:
 * the page's sidebar (`.jc-console .side`, the page's own selector) takes the band's height as padding at its top, so
 * its contents start below the traffic lights and its ground runs on under them. Nothing else of the page moves: its
 * strip, right of the sidebar, starts at the window's top edge. The page has two rules of its own for a Mac window,
 * keyed on a class (`html.desktop-mac`) this view never sets: this padding, which the sheet gives, and a drag region
 * over the sidebar's top 284 by 26 pixels (`#title-drag`), which the sheet leaves out, since no drag region of the page
 * is the window's (`noDragSheet`), and the band drags those same pixels.
 */
export const demoMacSheet = `.jc-console .side { padding-top: ${demoGeometry.macBand}px !important; }`;

/**
 * The band's sheet on macOS, of the user's origin and important, put into the band's document once it is loaded and
 * before either view is: the span cut out of its drag region is its right `macCut` pixels, the one number the layout
 * uses too. Until it lands the band's own stylesheet cuts out the whole band, so at no moment does the band drag where
 * the switch stands.
 */
export const demoBandSheet = `.cut { left: auto !important; width: ${demoGeometry.macCut}px !important; }`;

/**
 * The page's strip on Windows and Linux. The caption buttons stand over the strip's right end, and the page cannot know
 * they are there: it picks its sentence by the window's width alone (its short one at 1100 pixels and below) and lets a
 * sentence that does not fit end in an ellipsis of its own. So there the desktop puts two sheets of its own into the views,
 * each of the user's origin and important, which no rule of a page outranks: into the page's view, one that insets the
 * strip by the caption buttons' width, read from the overlay's geometry (`env(titlebar-area-*)`), and our switch beside
 * them, and has the page show its own short sentence wherever the strip so inset is too narrow for its long one; into our
 * switch's view, one that, where even the short sentence would reach the switch, makes the switch tighter: less padding
 * inside its two segments, the same words and the same height. Nothing of the page is edited, and no word of ours is
 * drawn over it.
 *
 * The numbers are the page's own and ours. The sentence's two ends are measured from the page as committed, in Chromium,
 * from the strip's left edge (its 16 pixels of padding included); the sentence is drawn in the Inter the page carries,
 * so they are the same on every system, while our switch's width is measured in Segoe UI. `test/demo-page.test.ts`
 * holds the page's rules they rest on. `margin` is the room kept beyond them before the switch takes its normal form or
 * the page its long sentence.
 */
const sentence = { short: 390.16, long: 604.5 } as const;
const page = { padStart: 16, gap: 8, shortUpTo: 1100 } as const;
const segment = { normal: 12, tight: 3 } as const;
/** Our switch, measured at its normal form: four segment paddings, its two words and its own padding of 2 each side. */
const normal = 106.16;
const ours = { padStart: 16, padEnd: 12, shadow: 5, normal, tight: normal - 4 * (segment.normal - segment.tight), segment } as const;
const margin = 8;
export const demoStrip = {
    sentence,
    page,
    ours,
    margin,
    /** The page's strip's right padding beyond the caption buttons: our tight switch, its shadow and padding, less the page's own gap. */
    inset: Math.floor(ours.padEnd + ours.tight + ours.shadow - page.gap),
    /** The page's strip's content width, so inset, from which its long sentence stands clear of our normal switch. */
    longFrom: Math.ceil(sentence.long + ours.padEnd + ours.normal + ours.shadow + margin - page.padStart - Math.floor(ours.padEnd + ours.tight + ours.shadow - page.gap)),
    /** Our switch's content width below which the switch takes its tight form: where the short sentence would reach its normal one. */
    tightBelow: Math.ceil(sentence.short + ours.padEnd + ours.normal + ours.shadow + margin - ours.padStart - ours.padEnd),
} as const;

/**
 * The two sheets on `platform`: on Windows and Linux, where the caption buttons stand over the strip. The page's sentence
 * is drawn in the font the page carries, so its numbers hold on both; our switch's are measured in Segoe UI, a Windows
 * font, so on Linux its words stay in the system's own font. Ubuntu's and Plasma's were seen to fit in the room kept
 * beside them (`margin`); a much wider font may put the sentence's last letters under the switch's edge.
 */
export function demoStripSheets(platform: NodeJS.Platform): { page: string; switch: string } | null {
    if (platform !== 'win32' && platform !== 'linux') return null;
    const caption = '100vw - env(titlebar-area-x, 0px) - env(titlebar-area-width, 100vw)';
    const tight = demoStrip.ours.segment.tight;
    return {
        page:
            `.jc-console #mode-bar { container: jason-strip / inline-size !important; padding-right: calc(${demoStrip.inset}px + ${caption}) !important; } ` +
            `@container jason-strip (width < ${demoStrip.longFrom}px) { .jc-console .mb-long { display: none !important; } .jc-console .mb-short { display: inline !important; } }`,
        switch:
            // Its widths are measured in Segoe UI, so on Windows its words are drawn in that font by name, never by the system's.
            (platform === 'win32' ? '.switch a, .switch span { font-family: "Segoe UI", sans-serif !important; } ' : '') +
            'body { container: jason-switch / inline-size !important; } ' +
            `@container jason-switch (width < ${demoStrip.tightBelow}px) { .switch a, .switch span { padding-left: ${tight}px !important; padding-right: ${tight}px !important; } }`,
    };
}

/**
 * The whole sheet put into the page's view on `platform`: on macOS the sidebar moved below the traffic lights
 * (`demoMacSheet`), on Windows and Linux the strip kept clear of the caption buttons (`demoStripSheets`), and
 * everywhere the note's repository address drawn as a link (`demoLinkSheet`).
 */
export function demoPageSheet(platform: NodeJS.Platform): string {
    if (platform === 'darwin') return `${demoMacSheet} ${demoLinkSheet}`;
    const sheets = demoStripSheets(platform);
    return sheets === null ? demoLinkSheet : `${sheets.page} ${demoLinkSheet}`;
}

/** What the strip shows at a window width: which sentence and switch, where the sentence ends and where the switch begins. */
export interface DemoStripAt {
    form: 'long' | 'short';
    switchForm: 'normal' | 'tight';
    /** Whether the sentence stands whole, with no ellipsis. */
    whole: boolean;
    /** Where the sentence ends, from the strip's left edge. */
    textEnd: number;
    /** Where our switch, its shadow of the strip's colour included, begins. */
    coveredFrom: number;
}

/**
 * Pure: the strip at a window's content width on `platform` with caption buttons this wide (0 where there are none), as
 * the page's rules and the sheets above lay it out. The tests hold every width from the window's least up to it.
 */
export function demoStripAt(platform: NodeJS.Platform, width: number, caption: number): DemoStripAt {
    const strip = width - demoGeometry.sidebar;
    const sheets = demoStripSheets(platform) !== null;
    const room = strip - caption;
    const pagePadEnd = sheets ? caption + demoStrip.inset : 12;
    const longByPage = width > demoStrip.page.shortUpTo;
    const longBySheet = !sheets || room - demoStrip.page.padStart - demoStrip.inset >= demoStrip.longFrom;
    const form = longByPage && longBySheet ? 'long' : 'short';
    const switchContent = strip - demoStrip.ours.padStart - demoStrip.ours.padEnd - caption;
    const switchForm = sheets && switchContent < demoStrip.tightBelow ? 'tight' : 'normal';
    const switchWidth = switchForm === 'tight' ? demoStrip.ours.tight : demoStrip.ours.normal;
    const limit = strip - pagePadEnd - demoStrip.page.gap;
    const end = demoStrip.sentence[form];
    return {
        form,
        switchForm,
        whole: end <= limit,
        textEnd: Math.min(end, limit),
        coveredFrom: strip - demoStrip.ours.padEnd - caption - switchWidth - demoStrip.ours.shadow,
    };
}
