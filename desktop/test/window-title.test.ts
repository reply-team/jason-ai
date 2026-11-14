// The name a person sees on the desktop's window, and so on its taskbar button: "Jason AI Harness", the console's own
// <title> too, and kept after a console older than the desktop, which calls itself "Jason", loads.

import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { keepWindowTitle, windowTitle } from '../src/window-title.ts';

/** A window as Electron behaves: a page's title replaces the window's unless a listener prevents it. */
class LoadingWindow extends EventEmitter {
    title: string;

    constructor(title: string) {
        super();
        this.title = title;
    }

    load(documentTitle: string): void {
        let prevented = false;
        this.emit('page-title-updated', { preventDefault: () => (prevented = true) }, documentTitle, true);
        if (!prevented) this.title = documentTitle;
    }
}

test('the window is called Jason AI Harness and keeps that title after an older console, whose own title is Jason, loads', () => {
    assert.equal(windowTitle, 'Jason AI Harness');
    const console = readFileSync(join(import.meta.dirname, '..', '..', 'ui', 'index.html'), 'utf8');
    assert.ok(console.includes(`<title>${windowTitle}</title>`), 'the console’s own title, the browser tab’s, is the same name');
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    const made = main.indexOf('new BrowserWindow({');
    assert.ok(made > 0, 'main.ts makes the window');
    assert.ok(main.slice(made, main.indexOf('webPreferences: {', made)).includes('title: windowTitle,'), 'the window is made with the name');
    assert.ok(main.includes('keepWindowTitle(window);'), 'the window keeps the name');

    const window = new LoadingWindow(windowTitle);
    keepWindowTitle(window);
    window.load('Jason');
    assert.equal(window.title, 'Jason AI Harness');
});
