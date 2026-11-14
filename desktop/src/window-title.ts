/**
 * The name a person sees on the desktop's window, and so on its taskbar button, and on the desktop's own dialogs. The
 * application's product name, its executable and its shortcut stay "Jason"; this is the name of the window alone.
 */
export const windowTitle = 'Jason AI Harness';

/** What of a window keeping its title needs: the event a page's own title raises. */
export interface TitledWindow {
    on(event: 'page-title-updated', listener: (event: { preventDefault(): void }, title: string, explicitSet: boolean) => void): unknown;
}

/**
 * Keeps the window's title when a page sets its own: the console's `<title>` is the browser tab's, and would otherwise
 * replace the window's name once it loads.
 */
export function keepWindowTitle(window: TitledWindow): void {
    window.on('page-title-updated', (event) => event.preventDefault());
}
