// What the desktop does differently on Linux and decides in pure code: which package it runs as, and how its window
// behaves with no tray to fall back on.

import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { keptAsideOf, launcherOf, linuxPackage, mountOf, recordAppImage, updaterOf, withoutMount } from '../src/linux-package.ts';
import { windowConduct } from '../src/login-item.ts';

test('the package is read as electron-updater reads it: package-type deb first, then an absolute APPIMAGE, else a folder', () => {
    assert.deepEqual(linuxPackage({}, 'deb\n'), { form: 'deb' });
    assert.deepEqual(linuxPackage({ APPIMAGE: '/home/a/Jason.AppImage' }, 'deb'), { form: 'deb' }, 'the updater is the deb one whatever APPIMAGE says');
    assert.deepEqual(linuxPackage({ APPIMAGE: '/home/a/Jason.AppImage' }, null), { form: 'appimage', file: '/home/a/Jason.AppImage' });
    assert.deepEqual(linuxPackage({ APPIMAGE: 'Jason.AppImage' }, null), { form: 'folder' }, 'a relative APPIMAGE is one the updater refuses');
    assert.deepEqual(linuxPackage({ APPIMAGE: '' }, null), { form: 'folder' });
    assert.deepEqual(linuxPackage({}, null), { form: 'folder' });
    assert.deepEqual(linuxPackage({ APPIMAGE: '/home/a/Jason.AppImage' }, 'rpm'), { form: 'folder' }, 'a package never built is no AppImage either');
});

test('the session starts the AppImage file itself, and the executable for any other form', () => {
    assert.equal(launcherOf({ form: 'appimage', file: '/home/a/Jason.AppImage' }, '/tmp/.mount_Jason/jason-desktop-app'), '/home/a/Jason.AppImage');
    assert.equal(launcherOf({ form: 'deb' }, '/opt/Jason/jason-desktop-app'), '/opt/Jason/jason-desktop-app');
    assert.equal(launcherOf({ form: 'folder' }, '/home/a/linux-unpacked/jason-desktop-app'), '/home/a/linux-unpacked/jason-desktop-app');
});

test('on Linux, where no tray can be counted on, closing the window quits, and a start at login opens minimized', () => {
    assert.deepEqual(windowConduct('linux'), { closeQuits: true, atLogin: 'minimized' });
    for (const platform of ['win32', 'darwin'] as const) {
        assert.deepEqual(windowConduct(platform), { closeQuits: false, atLogin: 'hidden' }, `${platform} keeps the tray`);
    }
});

test('no window shows a menu bar, Linux’s included: Live’s window and the Demo’s each hide it, on every platform', () => {
    const main = readFileSync(join(import.meta.dirname, '..', 'src', 'main.ts'), 'utf8');
    for (const start of ['async function run(', 'async function runDemo(']) {
        const from = main.indexOf(start);
        assert.ok(from >= 0, start);
        const body = main.slice(from, main.indexOf('\n}\n', from));
        assert.equal(body.split('new BrowserWindow({').length - 1, 1, `${start} makes one window`);
        assert.equal(body.split('window.setMenuBarVisibility(false);').length - 1, 1, `${start} hides its menu bar`);
    }
    assert.doesNotMatch(main, /setMenuBarVisibility\((?!false\))|autoHideMenuBar|setAutoHideMenuBar/);
});

test('the deb’s updater is off, since the system’s updates install the package, and every other form’s is as electron-updater makes it', () => {
    const updater = { isUpdaterActive: () => true, checked: 0 };
    assert.equal(updaterOf({ form: 'deb' }, updater).isUpdaterActive(), false);
    assert.equal(updaterOf({ form: 'appimage', file: '/home/a/Applications/Jason.AppImage' }, updater), updater);
    assert.equal(updaterOf({ form: 'folder' }, updater), updater);
    assert.equal(updaterOf(null, updater), updater);
});

test('an AppImage’s start records where its file is, beside the copies, for the command line, and writes it again only when it moved', (t) => {
    const desktop = mkdtempSync(join(tmpdir(), 'jason-desktop-record-'));
    t.after(() => rmSync(desktop, { recursive: true, force: true }));
    const record = join(desktop, 'appimage.json');
    recordAppImage(desktop, '/home/a/Applications/jason-desktop-linux-x64.AppImage');
    assert.deepEqual(JSON.parse(readFileSync(record, 'utf8')), { file: '/home/a/Applications/jason-desktop-linux-x64.AppImage' });
    const written = statSync(record).mtimeMs;
    recordAppImage(desktop, '/home/a/Applications/jason-desktop-linux-x64.AppImage');
    assert.equal(statSync(record).mtimeMs, written, 'the same file was recorded again');
    recordAppImage(desktop, '/home/a/Tools/Jason.AppImage');
    assert.deepEqual(JSON.parse(readFileSync(record, 'utf8')), { file: '/home/a/Tools/Jason.AppImage' });
});

test('what an update keeps aside: the AppImage file, nothing for the deb, the installed folder for any other form', () => {
    assert.equal(keptAsideOf('linux', { form: 'appimage', file: '/home/a/Jason.AppImage' }, '/tmp/.mount_Jason/jason-desktop-app'), '/home/a/Jason.AppImage');
    assert.equal(keptAsideOf('linux', { form: 'deb' }, '/opt/Jason/jason-desktop-app'), null);
    assert.equal(keptAsideOf('linux', { form: 'folder' }, '/home/a/Jason/jason-desktop-app'), '/home/a/Jason');
    assert.equal(keptAsideOf('darwin', null, '/Applications/Jason.app/Contents/MacOS/Jason'), '/Applications/Jason.app');
});

test('an AppImage’s mount reaches no child: its folders leave PATH, LD_LIBRARY_PATH, XDG_DATA_DIRS and GSETTINGS_SCHEMA_DIR, and the person’s own stay', () => {
    const appdir = '/tmp/.mount_JasonAb12';
    // As the pinned app-builder-lib's AppRun exports them (`appImageUtil.js`, `generateAppRunScript`).
    const env = {
        PATH: `${appdir}:${appdir}/usr/sbin:/home/a/.local/bin:/usr/bin`,
        LD_LIBRARY_PATH: `${appdir}/usr/lib:/opt/cuda/lib64`,
        XDG_DATA_DIRS: `${appdir}/usr/share/:/usr/share/gnome:/usr/local/share/:/usr/share/`,
        GSETTINGS_SCHEMA_DIR: `${appdir}/usr/share/glib-2.0/schemas`,
        HOME: '/home/a',
    };
    assert.deepEqual(withoutMount(env, appdir), {
        PATH: '/home/a/.local/bin:/usr/bin',
        LD_LIBRARY_PATH: '/opt/cuda/lib64',
        XDG_DATA_DIRS: '/usr/share/gnome:/usr/local/share/:/usr/share/',
        HOME: '/home/a',
    });
    assert.deepEqual(withoutMount({ PATH: `${appdir}-other/bin:/usr/bin` }, appdir), { PATH: `${appdir}-other/bin:/usr/bin` }, 'a folder beside the mount is not under it');
    assert.deepEqual(withoutMount(env, undefined), env, 'no AppImage, nothing taken');
    assert.equal(mountOf({ form: 'appimage', file: '/home/a/Jason.AppImage' }, { APPDIR: appdir }), appdir);
    assert.equal(mountOf({ form: 'appimage', file: '/home/a/Jason.AppImage' }, { APPDIR: 'relative' }), undefined);
    assert.equal(mountOf({ form: 'deb' }, { APPDIR: appdir }), undefined);
});
