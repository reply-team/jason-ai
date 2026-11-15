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
