// What the desktop does differently on Linux and decides in pure code: which package it runs as, and how its window
// behaves with no tray to fall back on.

import assert from 'node:assert/strict';
import { test } from 'node:test';
import { keptAsideOf, launcherOf, linuxPackage } from '../src/linux-package.ts';
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

test('on Linux, where no tray can be counted on, the menu bar shows, closing the window quits, and a start at login opens minimized', () => {
    assert.deepEqual(windowConduct('linux'), { menuBar: true, closeQuits: true, atLogin: 'minimized' });
    for (const platform of ['win32', 'darwin'] as const) {
        assert.deepEqual(windowConduct(platform), { menuBar: false, closeQuits: false, atLogin: 'hidden' }, `${platform} keeps the tray`);
    }
});

test('what an update keeps aside: the AppImage file, nothing for the deb, the installed folder for any other form', () => {
    assert.equal(keptAsideOf('linux', { form: 'appimage', file: '/home/a/Jason.AppImage' }, '/tmp/.mount_Jason/jason-desktop-app'), '/home/a/Jason.AppImage');
    assert.equal(keptAsideOf('linux', { form: 'deb' }, '/opt/Jason/jason-desktop-app'), null);
    assert.equal(keptAsideOf('linux', { form: 'folder' }, '/home/a/Jason/jason-desktop-app'), '/home/a/Jason');
    assert.equal(keptAsideOf('darwin', null, '/Applications/Jason.app/Contents/MacOS/Jason'), '/Applications/Jason.app');
});
