// The window's frame: on macOS no title bar, the window's buttons placed in the 26 px band the console leaves over its
// sidebar; on Windows and Linux the native frame. The window must stay movable whatever page it shows: on macOS the
// desktop puts a drag band of its own over the top of every page the window loads, an element drawn by a stylesheet of
// the user's origin that no page's rule outranks, and the page shown while the installation comes up has one too.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { EventEmitter } from 'node:events';
import { bandId, bandScript, bandStyle, frameOf, keepMovable, titleBand } from '../src/window-frame.ts';

const desktop = join(import.meta.dirname, '..');

test('on macOS the window has no title bar, and its buttons sit inside the band the console leaves for them', () => {
    const frame = frameOf('darwin');
    assert.equal(frame.titleBarStyle, 'hidden');
    assert.equal(titleBand, 26);
    const at = frame.trafficLightPosition!;
    // The three buttons are 12 px high: drawn from `y`, they stay inside the band, and left of the brand's 18 px inset.
    assert.ok(at.y >= 0 && at.y + 12 <= titleBand, `y ${at.y}`);
    assert.ok(at.x >= 8 && at.x <= 18, `x ${at.x}`);
    assert.deepEqual(Object.keys(frame).sort(), ['titleBarStyle', 'trafficLightPosition']);
});

test('on Windows and Linux the window keeps its native frame: nothing is set', () => {
    assert.deepEqual(frameOf('win32'), {});
    assert.deepEqual(frameOf('linux'), {});
});

test('every window is made with the frame of the platform it runs on, the Demo’s too', () => {
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    const windows = main.split('new BrowserWindow({').slice(1);
    assert.equal(windows.length, 2, 'Live’s window and the Demo’s');
    for (const made of windows) {
        const options = made.slice(0, made.indexOf('webPreferences'));
        assert.match(options, /\.\.\.frameOf\(process\.platform\),/);
    }
    assert.doesNotMatch(main, /titleBarStyle|trafficLightPosition|titleBarOverlay|frame:\s*false/);
});

test('the page shown while Jason starts is draggable by its top band, with nothing to click in it', () => {
    const page = readFileSync(join(desktop, 'shell', 'starting.html'), 'utf8');
    assert.match(page, /<body>\s*<div class="title-drag" aria-hidden="true"><\/div>/);
    const css = readFileSync(join(desktop, 'shell', 'shell.css'), 'utf8');
    const rule = /\.title-drag\s*\{([^}]*)\}/.exec(css)?.[1] ?? '';
    for (const declaration of ['position: fixed', 'top: 0', 'left: 0', 'right: 0', `height: ${titleBand}px`, '-webkit-app-region: drag'])
        assert.ok(rule.includes(declaration), declaration);
});

/** A window's contents as Electron's behave for this: `dom-ready` for each document, the sheets and scripts it takes. */
class Contents extends EventEmitter {
    inserted: { css: string; origin: string | undefined }[] = [];
    ran: string[] = [];
    refuse = false;

    insertCSS(css: string, options?: { cssOrigin?: 'user' | 'author' }): Promise<string> {
        if (this.refuse) return Promise.reject(new Error('the document is gone'));
        this.inserted.push({ css, origin: options?.cssOrigin });
        return Promise.resolve('key');
    }

    executeJavaScript(code: string): Promise<unknown> {
        if (this.refuse) return Promise.reject(new Error('the document is gone'));
        this.ran.push(code);
        return Promise.resolve(undefined);
    }
}

test('on macOS every document the window loads gets the desktop’s drag band: its element, and its user stylesheet', async () => {
    const contents = new Contents();
    keepMovable({ webContents: contents }, 'darwin');
    // The console, then a page that does not draw a band (an older or a custom console, a page that failed to load).
    contents.emit('dom-ready');
    contents.emit('dom-ready');
    assert.deepEqual(contents.inserted, [
        { css: bandStyle, origin: 'user' },
        { css: bandStyle, origin: 'user' },
    ]);
    assert.deepEqual(contents.ran, [bandScript, bandScript]);
    // A document gone before either lands refuses it; nothing throws.
    contents.refuse = true;
    contents.emit('dom-ready');
    await new Promise((settle) => setImmediate(settle));
});

test('the script adds one empty element, hidden from assistive technology, at the end of the document, once', () => {
    type Element = { id: string; attributes: Record<string, string>; setAttribute(name: string, value: string): void };
    const added: Element[] = [];
    const document = {
        getElementById: (id: string) => added.find((each) => each.id === id) ?? null,
        createElement: (tag: string): Element => {
            assert.equal(tag, 'div');
            const made: Element = { id: '', attributes: {}, setAttribute: (name, value) => (made.attributes[name] = value) };
            return made;
        },
        documentElement: { appendChild: (element: Element) => added.push(element) },
    };
    const run = new Function('document', bandScript) as (document: unknown) => void;
    run(document);
    run(document);
    assert.equal(added.length, 1);
    assert.equal(added[0]!.id, bandId);
    assert.deepEqual(added[0]!.attributes, { 'aria-hidden': 'true' });
});

test('the band is a box over the window buttons’ band, above everything, the drag region, and every declaration important', () => {
    const rule = new RegExp(`^#${bandId}\\s*\\{([^}]*)\\}$`).exec(bandStyle.trim());
    assert.ok(rule, bandStyle);
    const declarations = rule[1]!
        .split(';')
        .map((each) => each.trim())
        .filter((each) => each !== '');
    for (const each of declarations) assert.match(each, /!important$/, each);
    const said = Object.fromEntries(declarations.map((each) => each.replace(/\s*!important$/, '').split(/:\s*/)));
    assert.deepEqual(said, {
        display: 'block',
        visibility: 'visible',
        position: 'fixed',
        top: '0',
        left: '0',
        width: '284px',
        height: `${titleBand}px`,
        margin: '0',
        padding: '0',
        border: '0',
        background: 'transparent',
        'z-index': '2147483647',
        '-webkit-app-region': 'drag',
    });
});

test('on Windows and Linux, whose windows keep their frame, nothing is added', () => {
    for (const platform of ['win32', 'linux'] as const) {
        const contents = new Contents();
        keepMovable({ webContents: contents }, platform);
        contents.emit('dom-ready');
        assert.deepEqual(contents.inserted, []);
        assert.deepEqual(contents.ran, []);
        assert.equal(contents.listenerCount('dom-ready'), 0);
    }
});

test('the window keeps itself movable as it is made', () => {
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    assert.match(main, /keepMovable\(window, process\.platform\);/);
});
