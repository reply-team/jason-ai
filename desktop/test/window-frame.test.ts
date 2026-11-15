// The window's frame: on macOS no title bar, the window's buttons placed in the 26 px band the console leaves over its
// sidebar; on Windows and Linux the native frame. The window must stay movable whatever page it shows, so the page the
// application shows while the installation comes up carries a drag band of its own.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { frameOf, titleBand } from '../src/window-frame.ts';

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

test('the window is made with the frame of the platform it runs on', () => {
    const main = readFileSync(join(desktop, 'src', 'main.ts'), 'utf8');
    const made = main.slice(main.indexOf('new BrowserWindow({'));
    const options = made.slice(0, made.indexOf('webPreferences'));
    assert.match(options, /\.\.\.frameOf\(process\.platform\),/);
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
