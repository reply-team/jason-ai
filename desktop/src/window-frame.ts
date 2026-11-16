/**
 * The window's frame. On macOS the window has no title bar: its three buttons sit in a band of `titleBand` pixels the
 * console leaves over its sidebar (its `desktop-mac` class), and the window is dragged by that band and by the console's
 * top strip, which the page marks as its drag regions. On Windows the window has no title bar either: the system's
 * caption buttons are Electron's title bar overlay, standing over the right end of the console's top strip and as high
 * as it, on a ground of no colour so the strip shows through them, their symbols in the theme in force; the console
 * leaves their width free at the strip's right (its `desktop-win` class), reading it from the overlay's own geometry.
 * On Linux the window keeps its native frame, and nothing is set.
 */
export const titleBand = 26;

/** The console's top strip, right of its sidebar: on Windows the caption buttons are as high as it. */
export const stripHeight = 38;

/** The caption buttons' symbols: the console's ink in each theme. */
export const lightSymbols = '#0f172a';
export const darkSymbols = '#ececf1';

/** Electron's title bar overlay, as the window is made with it and as each change of theme sets it again. */
export interface Overlay {
    color: string;
    symbolColor: string;
    height: number;
}

/** What of Electron's window options the frame sets. */
export interface FrameOptions {
    titleBarStyle?: 'hidden';
    trafficLightPosition?: { x: number; y: number };
    titleBarOverlay?: Overlay;
}

/** The overlay in the theme in force: a ground of no colour at all, and the theme's ink for the symbols. */
export function overlayOf(dark: boolean): Overlay {
    return { color: '#00000000', symbolColor: dark ? darkSymbols : lightSymbols, height: stripHeight };
}

/** The frame of the window on `platform`, made while the theme in force is dark or not. */
export function frameOf(platform: NodeJS.Platform, dark: boolean): FrameOptions {
    // The buttons are 12 px high: drawn 7 px down they are centred in the band, and 12 px in they keep the brand's inset.
    if (platform === 'darwin') return { titleBarStyle: 'hidden', trafficLightPosition: { x: 12, y: 7 } };
    if (platform === 'win32') return { titleBarStyle: 'hidden', titleBarOverlay: overlayOf(dark) };
    return {};
}

/** What of a window following the theme needs: its overlay set again, whether it is gone, and its end. */
export interface OverlaidWindow {
    isDestroyed(): boolean;
    setTitleBarOverlay(options: Overlay): void;
    once(event: 'closed', listener: () => void): unknown;
}

/** What of Electron's `nativeTheme` the overlay reads: the theme in force, and the event that says it changed. */
export interface ThemeInForce {
    readonly shouldUseDarkColors: boolean;
    on(event: 'updated', listener: () => void): unknown;
    off(event: 'updated', listener: () => void): unknown;
}

/**
 * On Windows, sets the caption buttons' overlay again at each change of the theme in force, the person's choice or the
 * system's own change under the system's theme, while the window is open. Elsewhere there is no overlay to colour.
 */
export function followTheme(window: OverlaidWindow, theme: ThemeInForce, platform: NodeJS.Platform): void {
    if (platform !== 'win32') return;
    const follow = () => {
        if (!window.isDestroyed()) window.setTitleBarOverlay(overlayOf(theme.shouldUseDarkColors));
    };
    theme.on('updated', follow);
    window.once('closed', () => theme.off('updated', follow));
}

/**
 * The desktop's own drag band: on macOS over the window buttons' band at the top of the sidebar, on Windows just left of
 * the caption buttons and as high as they are. The console of this release draws the Mac band itself and leaves the
 * Windows one free at its strip's right; an older or a custom console, or a page that failed to load, may not, and the
 * window must stay movable whatever it shows. So the desktop puts an empty element of its own at the end of every
 * document the window loads, and draws it with a stylesheet of the user's origin whose every declaration is important,
 * which no rule of a page outranks: a page's stylesheet can neither hide nor move it.
 */
export const bandId = 'jason-desktop-drag';

export const bandStyle = `#${bandId} { display: block !important; visibility: visible !important; position: fixed !important; top: 0 !important; left: 0 !important; width: 284px !important; height: ${titleBand}px !important; margin: 0 !important; padding: 0 !important; border: 0 !important; background: transparent !important; z-index: 2147483647 !important; -webkit-app-region: drag !important; }`;

/** The Windows band's width, beside the caption buttons. */
export const windowsBandWidth = 40;

/**
 * The Windows band, placed by the overlay's own geometry: the free title bar area ends where the buttons begin, at any
 * scale of the display; with no overlay shown (a window in full screen) it falls back to the top right corner.
 */
export const windowsBandStyle = `#${bandId} { display: block !important; visibility: visible !important; position: fixed !important; top: env(titlebar-area-y, 0px) !important; left: calc(env(titlebar-area-x, 0px) + env(titlebar-area-width, 100vw) - ${windowsBandWidth}px) !important; width: ${windowsBandWidth}px !important; height: env(titlebar-area-height, ${stripHeight}px) !important; margin: 0 !important; padding: 0 !important; border: 0 !important; background: transparent !important; z-index: 2147483647 !important; -webkit-app-region: drag !important; }`;

/** The band of the window on `platform`, or none where the native frame moves the window. */
export function bandStyleOf(platform: NodeJS.Platform): string | null {
    if (platform === 'darwin') return bandStyle;
    if (platform === 'win32') return windowsBandStyle;
    return null;
}

/** The script that adds the band's element once to the document, after everything the page has. */
export const bandScript = `(() => { if (document.getElementById('${bandId}')) return; const band = document.createElement('div'); band.id = '${bandId}'; band.setAttribute('aria-hidden', 'true'); document.documentElement.appendChild(band); })();`;

/** What of a window keeping itself movable needs: its contents' event for each document, a sheet and a script. */
export interface MovableWindow {
    webContents: {
        on(event: 'dom-ready', listener: () => void): unknown;
        insertCSS(css: string, options?: { cssOrigin?: 'user' | 'author' }): Promise<string>;
        executeJavaScript(code: string): Promise<unknown>;
    };
}

/** On macOS and Windows, puts the desktop's drag band on every document the window loads; on Linux the native frame moves it. */
export function keepMovable(window: MovableWindow, platform: NodeJS.Platform): void {
    const style = bandStyleOf(platform);
    if (style === null) return;
    window.webContents.on('dom-ready', () => {
        // A document gone before either lands refuses it; the next document gets its own.
        window.webContents.insertCSS(style, { cssOrigin: 'user' }).catch(() => undefined);
        window.webContents.executeJavaScript(bandScript).catch(() => undefined);
    });
}
