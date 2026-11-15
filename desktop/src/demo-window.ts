/**
 * The Demo in the window: the page the application carries, in a view of its own sealed by `demo-page.ts`, and over it
 * the desktop's own Demo | Live switch, a second view added after it, so it is drawn on top, at the geometry where the
 * page draws a switch of its own (`switchBounds`). Nothing of an installation is here: no jason, no runtime, no chat, no
 * bridge, no folder made; the window's own contents stay empty under the two views.
 */

import { WebContentsView, type BrowserWindow } from 'electron';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { demoPage, demoSwitch, demoViewPreferences, onceEach, sealView, switchBounds, switchChoice, type DemoRefusal } from './demo-page.ts';

export interface DemoWindowDeps {
    /** The application's folder, where `demo/index.html` and `shell/demo-switch.html` are. */
    appPath: string;
    /** Told what a view refused, by kind, once each. */
    refused(view: 'page' | 'switch', kind: DemoRefusal): void;
    /** The person chose Live on our switch. */
    live(): void;
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
    await sealView(page.webContents, demoPage, { read: fileOf(pageFile), refused: onceEach((kind) => deps.refused('page', kind)) });
    await sealView(ours.webContents, demoSwitch, {
        read: fileOf(switchFile),
        refused: onceEach((kind) => deps.refused('switch', kind)),
        asked: (url) => {
            if (switchChoice(url) === 'live') deps.live();
        },
    });
    // The page first, then ours: a view added later is drawn over the ones before it.
    window.contentView.addChildView(page);
    window.contentView.addChildView(ours);
    const layout = () => {
        const { width, height } = window.getContentBounds();
        page.setBounds({ x: 0, y: 0, width, height });
        ours.setBounds(switchBounds(width));
    };
    layout();
    window.on('resize', layout);
    const close = () => {
        if (!page.webContents.isDestroyed()) page.webContents.close();
        if (!ours.webContents.isDestroyed()) ours.webContents.close();
    };
    window.on('closed', close);
    try {
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
