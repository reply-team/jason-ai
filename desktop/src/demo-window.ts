/**
 * The Demo in the window: the page the application carries, in a view of its own sealed by `demo-page.ts`, and over it
 * the desktop's own Demo | Live switch, a second view added after it, so it is drawn on top, over the strip whose right
 * end the page leaves free for one (`demoLayout`); that view is transparent but for the switch. Nothing of an
 * installation is here: no jason, no runtime, no chat, no bridge, no folder made. The window's own contents are empty
 * under the two views, but on macOS, where they hold our band (`shell/demo-band.html`, no script), the window's drag
 * region over its top 26 pixels, under the page that starts at the window's top edge. Electron asks the window's own
 * contents first whether a point is in a drag region, whichever view is drawn over it, so the band's document drags
 * over the band's height alone, and never over our switch's view. The page's view also carries the desktop's link on
 * the repository's address in the page's note, and that address alone leaves it, to the person's browser
 * (`demoRepository`).
 */

import { WebContentsView, type BrowserWindow } from 'electron';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import {
    demoLayout,
    demoLinkOpener,
    demoLinkScript,
    demoPage,
    demoPageSheet,
    demoStripSheets,
    demoSwitch,
    demoViewPreferences,
    onceEach,
    sealView,
    switchChoice,
    watchDemoGestures,
    type DemoRefusal,
} from './demo-page.ts';

export interface DemoWindowDeps {
    /** The application's folder, where `demo/index.html`, `shell/demo-switch.html` and `shell/demo-band.html` are. */
    appPath: string;
    /** The platform the window is drawn on: on macOS the views start below our band. */
    platform: NodeJS.Platform;
    /** Told what a view refused, by kind, once each. */
    refused(view: 'page' | 'switch', kind: DemoRefusal): void;
    /** The person chose Live on our switch. */
    live(): void;
    /** Opens the repository's address, the one the note names (`demoRepository`), in the person's browser; it is handed no address. */
    open(): void;
    /** The page could not be read or loaded: its views are taken off, and the window says so instead. */
    unshown(): void;
}

export interface DemoViews {
    /** Loads the page again: Reload in the menu. */
    reload(): void;
}

/** A file of the application's own, read whole at each request for it; never a file the page names. */
function fileOf(path: string): () => Uint8Array {
    return () => readFileSync(path);
}

/** Makes both views on the window, seals each before anything is loaded into it, and loads them. */
export async function openDemo(window: BrowserWindow, deps: DemoWindowDeps): Promise<DemoViews> {
    const pageFile = join(deps.appPath, 'demo', 'index.html');
    const switchFile = join(deps.appPath, 'shell', 'demo-switch.html');
    const bandFile = join(deps.appPath, 'shell', 'demo-band.html');
    const none: DemoViews = { reload: () => undefined };
    try {
        readFileSync(pageFile);
        readFileSync(switchFile);
    } catch {
        deps.unshown();
        return none;
    }
    const page = new WebContentsView({ webPreferences: demoViewPreferences(demoPage) });
    const ours = new WebContentsView({ webPreferences: demoViewPreferences(demoSwitch) });
    // On Windows and Linux, the strip kept clear of the caption buttons (`demoStripSheets`); on macOS the sidebar moved
    // below the traffic lights (`demoMacSheet`).
    const sheets = demoStripSheets(deps.platform);
    // The note's repository address drawn as a link, and the one address the page's view hands to the browser.
    await sealView(page.webContents, demoPage, {
        read: fileOf(pageFile),
        refused: onceEach((kind) => deps.refused('page', kind)),
        sheet: demoPageSheet(deps.platform),
        script: demoLinkScript,
        asked: demoLinkOpener(deps.open, watchDemoGestures(page.webContents)),
    });
    await sealView(ours.webContents, demoSwitch, {
        read: fileOf(switchFile),
        sheet: sheets?.switch,
        refused: onceEach((kind) => deps.refused('switch', kind)),
        asked: (url) => {
            if (switchChoice(url) !== 'live') return false;
            deps.live();
            return true;
        },
    });
    // Ours is transparent, so the page's strip shows through it but for the switch.
    ours.setBackgroundColor('#00000000');
    // The page first, then ours: a view added later is drawn over the ones before it.
    window.contentView.addChildView(page);
    window.contentView.addChildView(ours);
    const layout = () => {
        const { width, height } = window.getContentBounds();
        const at = demoLayout(deps.platform, width, height);
        page.setBounds(at.page);
        ours.setBounds(at.switch);
    };
    layout();
    window.on('resize', layout);
    const close = () => {
        if (!page.webContents.isDestroyed()) page.webContents.close();
        if (!ours.webContents.isDestroyed()) ours.webContents.close();
    };
    window.on('closed', close);
    try {
        // On macOS the band, the window's own contents under the views, drags the window; elsewhere they stay empty.
        if (deps.platform === 'darwin') await window.webContents.loadFile(bandFile);
        await Promise.all([page.webContents.loadURL(demoPage.url), ours.webContents.loadURL(demoSwitch.url)]);
    } catch {
        window.contentView.removeChildView(ours);
        window.contentView.removeChildView(page);
        close();
        deps.unshown();
        return none;
    }
    return { reload: () => page.webContents.reload() };
}
