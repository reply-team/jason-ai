/**
 * The window's frame. On macOS the window has no title bar: its three buttons sit in a band of `titleBand` pixels the
 * console leaves over its sidebar (its `desktop-mac` class), and the window is dragged by that band and by the console's
 * top strip, which the page marks as its drag regions. On Windows and Linux the window keeps its native frame, and
 * nothing is set.
 */
export const titleBand = 26;

/** What of Electron's window options the frame sets. */
export interface FrameOptions {
    titleBarStyle?: 'hidden';
    trafficLightPosition?: { x: number; y: number };
}

/** The frame of the window on `platform`. */
export function frameOf(platform: NodeJS.Platform): FrameOptions {
    if (platform !== 'darwin') return {};
    // The buttons are 12 px high: drawn 7 px down they are centred in the band, and 12 px in they keep the brand's inset.
    return { titleBarStyle: 'hidden', trafficLightPosition: { x: 12, y: 7 } };
}

/**
 * The desktop's own drag band, over the window buttons' band at the top of the sidebar. The console of this release
 * draws the same band itself; an older or a custom console, or a page that failed to load, may not, and the window must
 * stay movable whatever it shows. So the desktop puts an empty element of its own at the end of every document the
 * window loads, and draws it with a stylesheet of the user's origin whose every declaration is important, which no
 * rule of a page outranks: a page's stylesheet can neither hide nor move it.
 */
export const bandId = 'jason-desktop-drag';

export const bandStyle = `#${bandId} { display: block !important; visibility: visible !important; position: fixed !important; top: 0 !important; left: 0 !important; width: 284px !important; height: ${titleBand}px !important; margin: 0 !important; padding: 0 !important; border: 0 !important; background: transparent !important; z-index: 2147483647 !important; -webkit-app-region: drag !important; }`;

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

/** On macOS, puts the desktop's drag band on every document the window loads; elsewhere the native frame moves it. */
export function keepMovable(window: MovableWindow, platform: NodeJS.Platform): void {
    if (platform !== 'darwin') return;
    window.webContents.on('dom-ready', () => {
        // A document gone before either lands refuses it; the next document gets its own.
        window.webContents.insertCSS(bandStyle, { cssOrigin: 'user' }).catch(() => undefined);
        window.webContents.executeJavaScript(bandScript).catch(() => undefined);
    });
}
