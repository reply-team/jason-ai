// The window's frame: on macOS no title bar, the window's buttons placed in the 26 px band the console leaves over its
// sidebar; on Windows and Linux no title bar either, the caption buttons standing over the right end of the console's
// 38 px strip, their symbols in the theme in force. The window must stay movable whatever page it shows: the desktop
// puts a drag band of its own on every page the window loads, an element drawn by a stylesheet of the user's origin
// that no page's rule outranks, and the page shown while the installation comes up has one too.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { EventEmitter } from 'node:events';
import { bandId, bandScript, bandStyle, bandStyleOf, darkSymbols, followTheme, frameOf, keepMovable, lightSymbols, overlayOf, stripHeight, titleBand, overlayBandStyle, overlayBandWidth } from '../src/window-frame.ts';

const desktop = join(import.meta.dirname, '..');

test('on macOS the window has no title bar, and its buttons sit inside the band the console leaves for them', () => {
    const frame = frameOf('darwin', false);
    assert.deepEqual(frameOf('darwin', true), frame, 'the theme does not move them');
    assert.equal(frame.titleBarStyle, 'hidden');
    assert.equal(titleBand, 26);
    const at = frame.trafficLightPosition!;
    // The three buttons are 12 px high: drawn from `y`, they stay inside the band, and left of the brand's 18 px inset.
    assert.ok(at.y >= 0 && at.y + 12 <= titleBand, `y ${at.y}`);
    assert.ok(at.x >= 8 && at.x <= 18, `x ${at.x}`);
    assert.deepEqual(Object.keys(frame).sort(), ['titleBarStyle', 'trafficLightPosition']);
});

test('on Windows and Linux the title bar is hidden and the caption buttons stand over the strip, as high as it, on its own ground', () => {
    assert.equal(stripHeight, 38);
    for (const platform of ['win32', 'linux'] as const) {
        for (const dark of [false, true]) {
            const frame = frameOf(platform, dark);
            assert.deepEqual(frame, { titleBarStyle: 'hidden', titleBarOverlay: overlayOf(dark) }, `${platform} ${dark}`);
        }
    }
    // The overlay's ground is transparent, so the strip it stands on shows through in either mode and theme; its symbols
    // are the theme's ink.
    assert.deepEqual(overlayOf(false), { color: '#00000000', symbolColor: lightSymbols, height: 38 });
    assert.deepEqual(overlayOf(true), { color: '#00000000', symbolColor: darkSymbols, height: 38 });
    assert.notEqual(lightSymbols, darkSymbols);
    for (const colour of [lightSymbols, darkSymbols]) assert.match(colour, /^#[0-9a-f]{6}$/);
});

/** The theme as Electron's `nativeTheme` behaves for this: `updated` after each change, and the colours it reads. */
class Theme extends EventEmitter {
    shouldUseDarkColors = false;
    change(dark: boolean): void {
        this.shouldUseDarkColors = dark;
        this.emit('updated');
    }
}

/** A window as far as following the theme goes: the overlays it is given, and whether it is gone. */
class Overlaid extends EventEmitter {
    given: unknown[] = [];
    destroyed = false;
    isDestroyed(): boolean {
        return this.destroyed;
    }
    setTitleBarOverlay(options: unknown): void {
        if (this.destroyed) throw new Error('Object has been destroyed');
        this.given.push(options);
    }
}

test('on Windows and Linux the caption buttons follow the theme in force as it changes, while the window is open', () => {
    for (const platform of ['win32', 'linux'] as const) {
        const theme = new Theme();
        const window = new Overlaid();
        followTheme(window, theme, platform);
        assert.deepEqual(window.given, [], 'the window was made in the theme in force');
        theme.change(true);
        theme.change(false);
        // The system's own change, under the system's theme, reaches it the same way.
        theme.change(true);
        assert.deepEqual(window.given, [overlayOf(true), overlayOf(false), overlayOf(true)], platform);
        for (const each of window.given) assert.deepEqual(Object.keys(each as object).sort(), ['color', 'height', 'symbolColor']);
    }
});

test('a window closed stops following the theme, and one already gone is never touched', () => {
    for (const platform of ['win32', 'linux'] as const) {
        const theme = new Theme();
        const window = new Overlaid();
        followTheme(window, theme, platform);
        window.destroyed = true;
        theme.change(true);
        assert.deepEqual(window.given, [], platform);
        window.emit('closed');
        assert.equal(theme.listenerCount('updated'), 0, platform);
    }
});

test('on macOS nothing follows the theme: there is no overlay to colour', () => {
    const theme = new Theme();
    const window = new Overlaid();
    followTheme(window, theme, 'darwin');
    theme.change(true);
    assert.deepEqual(window.given, []);
    assert.equal(theme.listenerCount('updated'), 0);
});

test('every window is made with the frame of the platform it runs on, the Demo’s too', () => {
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    const windows = main.split('new BrowserWindow({').slice(1);
    assert.equal(windows.length, 2, 'Live’s window and the Demo’s');
    for (const made of windows) {
        const options = made.slice(0, made.indexOf('webPreferences'));
        assert.match(options, /\.\.\.frameOf\(process\.platform, nativeTheme\.shouldUseDarkColors\),/);
    }
    assert.doesNotMatch(main, /titleBarStyle|trafficLightPosition|titleBarOverlay|setTitleBarOverlay|frame:\s*false/);
});

test('the theme in force is set before the frame’s options are built, in Live and in the Demo, so no first paint is in the other', () => {
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    const frame = '...frameOf(process.platform, nativeTheme.shouldUseDarkColors),';
    for (const [start, set] of [
        ['async function run(', 'nativeTheme.themeSource = startingTheme('],
        ['async function runDemo(', "nativeTheme.themeSource = 'light';"],
    ] as const) {
        const from = main.indexOf(start);
        assert.ok(from >= 0, start);
        const body = main.slice(from, main.indexOf('\n}\n', from));
        const setAt = body.indexOf(set);
        const frameAt = body.indexOf(frame);
        assert.ok(setAt >= 0 && frameAt >= 0, `${start} sets the theme and builds the frame`);
        assert.ok(setAt < frameAt, `${start} sets the theme first`);
        assert.ok(!body.slice(0, frameAt).slice(setAt + set.length).includes('nativeTheme.themeSource ='), `${start}: nothing sets it again between`);
    }
});

test('the console leaves the band beside the caption buttons its width: the strip’s padding names the same number as the band', () => {
    const css = readFileSync(join(desktop, '..', 'ui', 'src', 'console', 'console.css'), 'utf8');
    const rule = /\.jc-console\.desktop-overlay \.mode-strip\s*\{([^}]*)\}/.exec(css)?.[1] ?? '';
    const padding = /padding-right:\s*calc\(12px \+ (\d+)px \+ 100vw - env\(titlebar-area-x, 0px\) - env\(titlebar-area-width, 100vw\)\);/.exec(rule);
    assert.ok(padding, rule);
    assert.equal(Number(padding[1]), overlayBandWidth);
});

test('Live’s window follows the theme as it is made; the Demo’s is drawn light and never changes it', () => {
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    assert.equal(main.split('followTheme(window, nativeTheme, process.platform);').length - 1, 1);
    const demo = main.slice(main.indexOf('async function runDemo('));
    assert.doesNotMatch(demo.slice(0, demo.indexOf('\n}\n')), /followTheme/);
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

test('on Windows and Linux every document the window loads gets the desktop’s drag band too, beside the caption buttons', () => {
    assert.equal(bandStyleOf('darwin'), bandStyle);
    assert.equal(bandStyleOf('win32'), overlayBandStyle);
    assert.equal(bandStyleOf('linux'), overlayBandStyle);
    for (const platform of ['win32', 'linux'] as const) {
        const contents = new Contents();
        keepMovable({ webContents: contents }, platform);
        contents.emit('dom-ready');
        contents.emit('dom-ready');
        assert.deepEqual(contents.inserted, [
            { css: overlayBandStyle, origin: 'user' },
            { css: overlayBandStyle, origin: 'user' },
        ]);
        assert.deepEqual(contents.ran, [bandScript, bandScript]);
    }
});

test('the band beside the caption buttons stands left of them, as high as they are, read from the overlay’s own geometry', () => {
    const rule = new RegExp(`^#${bandId}\\s*\\{([^}]*)\\}$`).exec(overlayBandStyle.trim());
    assert.ok(rule, overlayBandStyle);
    const declarations = rule[1]!
        .split(';')
        .map((each) => each.trim())
        .filter((each) => each !== '');
    for (const each of declarations) assert.match(each, /!important$/, each);
    const said = Object.fromEntries(
        declarations.map((each) => {
            const at = each.indexOf(':');
            return [each.slice(0, at).trim(), each.slice(at + 1).replace(/\s*!important$/, '').trim()];
        }),
    );
    assert.equal(overlayBandWidth, 40);
    assert.deepEqual(said, {
        display: 'block',
        visibility: 'visible',
        position: 'fixed',
        // The free title bar area ends where the buttons begin; without an overlay the band falls back to the corner.
        top: 'env(titlebar-area-y, 0px)',
        left: `calc(env(titlebar-area-x, 0px) + env(titlebar-area-width, 100vw) - ${overlayBandWidth}px)`,
        width: `${overlayBandWidth}px`,
        height: `env(titlebar-area-height, ${stripHeight}px)`,
        margin: '0',
        padding: '0',
        border: '0',
        background: 'transparent',
        'z-index': '2147483647',
        '-webkit-app-region': 'drag',
    });
});

test('the window keeps itself movable as it is made', () => {
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    assert.match(main, /keepMovable\(window, process\.platform\);/);
});
