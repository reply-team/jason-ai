/**
 * The name a person sees on the desktop's window, and so on its taskbar button, on the desktop's own dialogs and on the
 * tray's tooltip. The application's product name, its executable and its shortcut stay "Jason"; this is the name of
 * what the window and the tray show alone.
 */
export const windowTitle = 'Jason AI Harness';

/** What of a window keeping its title needs: the event a page's own title raises. */
export interface TitledWindow {
    on(event: 'page-title-updated', listener: (event: { preventDefault(): void }, title: string, explicitSet: boolean) => void): unknown;
}

/**
 * Keeps the window's title when a page sets its own: the console's `<title>` is the browser tab's, and a console older
 * than this desktop, which an installation may still serve, calls itself "Jason"; it would otherwise replace the
 * window's name once it loads.
 */
export function keepWindowTitle(window: TitledWindow): void {
    window.on('page-title-updated', (event) => event.preventDefault());
}
