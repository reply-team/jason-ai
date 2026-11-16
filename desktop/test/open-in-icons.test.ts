// "Open in", each application's own icon: the icon set and property list readers on bytes made here (never an
// application's own file), the reader's cache and its wait, and the places that carry an icon on each system.

import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { crc32, deflateSync } from 'node:zlib';
import { test } from 'node:test';
import { edgeAtMost, icnsBytesAtMost, iconFileOf, largestPng, plistBytesAtMost } from '../src/open-in-icns.ts';
import { IconReader, iconOf, pixelEdge, readRegularFileAtMost, rgbaOf, type IconImage, type IconReaderDeps, type PlaceIcon } from '../src/open-in-icons.ts';
import { finderApp, iconPath, OpenIn, terminalApp, type OpenInDeps, type Probes } from '../src/open-in.ts';

// ---- Bytes made here ------------------------------------------------------------------------------------------------

function pngChunk(type: string, data: Buffer): Buffer {
    const head = Buffer.alloc(8);
    head.writeUInt32BE(data.length, 0);
    head.write(type, 4, 'latin1');
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), data])) >>> 0, 0);
    return Buffer.concat([head, data, crc]);
}

/** A PNG of one colour, `edge` pixels a side, RGBA. */
function png(edge: number): Buffer {
    const header = Buffer.alloc(13);
    header.writeUInt32BE(edge, 0);
    header.writeUInt32BE(edge, 4);
    header.set([8, 6, 0, 0, 0], 8);
    const row = Buffer.concat([Buffer.from([0]), Buffer.alloc(edge * 4, 0x80)]);
    const pixels = deflateSync(Buffer.concat(Array.from({ length: edge }, () => row)));
    return Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), pngChunk('IHDR', header), pngChunk('IDAT', pixels), pngChunk('IEND', Buffer.alloc(0))]);
}

/** An icon set of the chunks given, each a type and its data. */
function icns(...chunks: [string, Buffer][]): Buffer {
    const parts = chunks.map(([type, data]) => {
        const head = Buffer.alloc(8);
        head.write(type, 0, 'latin1');
        head.writeUInt32BE(data.length + 8, 4);
        return Buffer.concat([head, data]);
    });
    const head = Buffer.alloc(8);
    head.write('icns', 0, 'latin1');
    head.writeUInt32BE(8 + parts.reduce((sum, part) => sum + part.length, 0), 4);
    return Buffer.concat([head, ...parts]);
}

const plist = (body: string) => `<?xml version="1.0" encoding="UTF-8"?>\n<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">\n<plist version="1.0">\n<dict>\n${body}\n</dict>\n</plist>\n`;

// ---- The icon set ---------------------------------------------------------------------------------------------------

test('an icon set gives the PNG data of its largest image at most 512 px, and of the largest where none is', () => {
    const small = png(4);
    const middle = png(16);
    const large = png(600);
    assert.deepEqual(largestPng(icns(['ic07', small])), small);
    assert.deepEqual(largestPng(icns(['ic07', small], ['ic09', middle], ['ic10', large])), middle, 'the largest at most 512');
    assert.deepEqual(largestPng(icns(['ic10', large], ['TOC ', Buffer.alloc(8)])), large, 'the largest when none is at most 512');
    // The size is read from the PNG's own header, not from the chunk's type.
    assert.deepEqual(largestPng(icns(['ic09', small], ['ic07', middle])), middle);
});

test('an icon set cut short gives the chunks that are whole before the cut, and none where nothing is whole', () => {
    const good = png(8);
    const whole = icns(['ic07', good], ['ic08', png(16)]);
    assert.deepEqual(largestPng(whole.subarray(0, whole.length - 10)), good, 'the cut chunk ends the walk');
    const one = icns(['ic08', good]);
    assert.equal(largestPng(one.subarray(0, one.length - 1)), null);
    assert.equal(largestPng(one.subarray(0, 6)), null);
});

test('a chunk that is not PNG data, an old chunk type and a file that is not an icon set give nothing', () => {
    // JPEG 2000 in an old set's ic09, a bitmap chunk of the old kind, and bytes that only start like a PNG.
    const jpeg2000 = Buffer.from([0x00, 0x00, 0x00, 0x0c, 0x6a, 0x50, 0x20, 0x20, 0x0d, 0x0a, 0x87, 0x0a, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
    assert.equal(largestPng(icns(['ic09', jpeg2000])), null);
    assert.equal(largestPng(icns(['il32', png(8)])), null, 'only the types that hold PNG data are read');
    assert.equal(largestPng(icns(['ic08', png(8).subarray(0, 20)])), null);
    assert.equal(largestPng(png(8)), null);
    assert.equal(largestPng(Buffer.alloc(0)), null);
    // A chunk claiming a length shorter than its own header ends the walk rather than looping.
    const looping = icns(['ic07', png(4)]);
    looping.writeUInt32BE(4, 12);
    assert.equal(largestPng(looping), null);
});

// ---- The property list ----------------------------------------------------------------------------------------------

test('an XML property list names its icon file, with .icns added where the name has none', () => {
    assert.equal(iconFileOf(plist('<key>CFBundleName</key>\n<string>Editor</string>\n<key>CFBundleIconFile</key>\n<string>Editor.icns</string>')), 'Editor.icns');
    assert.equal(iconFileOf(plist('<key>CFBundleIconFile</key>\n\t<string>Editor</string>')), 'Editor.icns');
    assert.equal(iconFileOf(plist('<key>CFBundleIconFile</key><string>A &amp; B</string>')), 'A & B.icns');
});

test('a binary property list, a list with no icon file, and a name that is not one file name give nothing', () => {
    assert.equal(iconFileOf('bplist00\u00d1\u0001\u0002_\u0010\u0010CFBundleIconFile'), null);
    // An application whose icon is only in an asset catalog names it by CFBundleIconName, which is not read.
    assert.equal(iconFileOf(plist('<key>CFBundleIconName</key>\n<string>AppIcon</string>')), null);
    for (const name of ['../../etc/x', 'a/b', 'a\\b', '', '..']) assert.equal(iconFileOf(plist(`<key>CFBundleIconFile</key><string>${name}</string>`)), null, name);
    assert.equal(iconFileOf('CFBundleIconFile = Editor;'), null, 'an old text list is not XML');
});

// ---- Pixels ---------------------------------------------------------------------------------------------------------

test('the pixels the page gets are RGBA, not premultiplied, from the BGRA premultiplied Skia keeps', () => {
    // Opaque red, half-covered white, and nothing.
    const bgra = Buffer.from([0, 0, 255, 255, 128, 128, 128, 128, 0, 0, 0, 0]);
    assert.deepEqual([...rgbaOf(bgra)], [255, 0, 0, 255, 255, 255, 255, 128, 0, 0, 0, 0]);
});

/** A stand-in for Electron's image: its size and the BGRA it holds. */
function image(width: number, height: number, empty = false): IconImage & { resized: { width: number; height: number }[] } {
    const made = {
        resized: [] as { width: number; height: number }[],
        isEmpty: () => empty,
        getSize: () => ({ width, height }),
        resize: (options: { width: number; height: number }) => {
            made.resized.push({ width: options.width, height: options.height });
            return image(options.width, options.height);
        },
        toBitmap: () => Buffer.alloc(width * height * 4, 255),
    };
    return made;
}

test('an empty image, or pixels that do not fill its size, is no icon', () => {
    assert.equal(iconOf(image(2, 2, true)), null);
    assert.equal(iconOf({ ...image(2, 2), toBitmap: () => Buffer.alloc(3) }), null);
    assert.deepEqual(iconOf(image(1, 1)), { width: 1, height: 1, rgba: Buffer.from([255, 255, 255, 255]).toString('base64') });
});

// ---- The reader -----------------------------------------------------------------------------------------------------

interface Planted {
    deps: IconReaderDeps;
    calls: string[];
    files: Map<string, Buffer>;
    times: Map<string, number>;
    /** Paths that are no regular file: a device or a pipe, which a stat says has no size. */
    special: Set<string>;
    /** Paths whose file grows past its cap between the stat and the read. */
    growing: Set<string>;
}

function planted(platform: NodeJS.Platform, overrides: Partial<IconReaderDeps> = {}): Planted {
    const calls: string[] = [];
    const files = new Map<string, Buffer>();
    const times = new Map<string, number>();
    const special = new Set<string>();
    const growing = new Set<string>();
    const deps: IconReaderDeps = {
        platform,
        stat: async (path) => {
            calls.push(`stat ${path}`);
            const bytes = files.get(path);
            if (bytes === undefined && !times.has(path) && !special.has(path)) throw new Error('ENOENT');
            return { mtimeMs: times.get(path) ?? 1, size: bytes?.length ?? 0, isFile: () => bytes !== undefined && !special.has(path) };
        },
        readAtMost: async (path, cap) => {
            calls.push(`read ${path}`);
            const bytes = files.get(path);
            if (bytes === undefined) throw new Error('ENOENT');
            return special.has(path) || growing.has(path) || bytes.length > cap ? null : bytes;
        },
        fileIcon: async (path) => {
            calls.push(`fileIcon ${path}`);
            return image(32, 32);
        },
        fromBuffer: (bytes) => {
            calls.push(`fromBuffer ${bytes.length}`);
            return image(256, 256);
        },
        ...overrides,
    };
    return { deps, calls, files, times, special, growing };
}

const exe = 'C:\\Programs\\Editor\\Editor.exe';

test('on Windows an icon is read once a run for a path, again when its file changes, one answer kept a path, and a failed read is asked again', async () => {
    const machine = planted('win32');
    machine.times.set(exe, 1);
    const reader = new IconReader(machine.deps);
    const asked = () => machine.calls.filter((call) => call.startsWith('fileIcon')).length;
    const first = await reader.icon(exe);
    assert.equal(first?.width, 32);
    assert.equal(first?.height, 32);
    assert.deepEqual(await reader.icon(exe), first);
    assert.equal(asked(), 1, 'kept for the run');
    machine.times.set(exe, 2);
    await reader.icon(exe);
    assert.equal(asked(), 2, 'read again once the file changed');
    // Only the last answer is kept for a path: the earlier time, back again, is read again rather than found.
    machine.times.set(exe, 1);
    await reader.icon(exe);
    assert.equal(asked(), 3, 'one answer a path, so the cache never grows');

    let failures = 0;
    const failing = planted('win32', {
        fileIcon: async () => {
            failures += 1;
            throw new Error('no icon');
        },
    });
    failing.times.set(exe, 1);
    const unlucky = new IconReader(failing.deps);
    assert.equal(await unlucky.icon(exe), null);
    assert.equal(await unlucky.icon(exe), null);
    assert.equal(failures, 2, 'a read that failed is no answer, so it is asked again');
    // A path that is not there is none, and nothing is asked of the system.
    assert.equal(await new IconReader(planted('win32').deps).icon('C:\\Gone\\Gone.exe'), null);
});

test('a read slower than the wait is not waited for, and its icon is there the next time', async () => {
    let finish: (value: IconImage) => void = () => {};
    const machine = planted('win32', { waitMs: 20, fileIcon: () => new Promise<IconImage>((resolve) => (finish = resolve)) });
    machine.times.set(exe, 1);
    const reader = new IconReader(machine.deps);
    const started = Date.now();
    assert.equal(await reader.icon(exe), null);
    assert.ok(Date.now() - started < 1000, 'the menu is not held');
    finish(image(32, 32));
    assert.equal((await reader.icon(exe))?.width, 32);
});

test('on Linux no icon is read', async () => {
    const machine = planted('linux');
    assert.equal(await new IconReader(machine.deps).icon('/usr/bin/code'), null);
    assert.deepEqual(machine.calls, []);
});

const app = '/Applications/Editor.app';

test('on macOS the icon is the bundle’s own icon set, its largest PNG decoded and resized to twice the drawn size', async () => {
    const machine = planted('darwin');
    const inside = png(16);
    machine.times.set(app, 1);
    machine.files.set(`${app}/Contents/Info.plist`, Buffer.from(plist('<key>CFBundleIconFile</key><string>Editor</string>')));
    machine.files.set(`${app}/Contents/Resources/Editor.icns`, icns(['ic07', png(4)], ['ic08', inside]));
    const icon = await new IconReader(machine.deps).icon(app);
    assert.deepEqual([icon?.width, icon?.height], [pixelEdge, pixelEdge]);
    assert.ok(machine.calls.includes(`fromBuffer ${inside.length}`), 'the largest chunk is decoded');
    assert.equal(machine.calls.filter((call) => call.startsWith('fileIcon')).length, 0, 'the system’s generic icon is never asked for');
});

test('on macOS a binary list, an asset catalog only, a missing or an oversized icon set leave the glyph, the set unread when too large', async () => {
    const cases: [string, (machine: Planted) => void][] = [
        ['binary', (machine) => machine.files.set(`${app}/Contents/Info.plist`, Buffer.from('bplist00 binary'))],
        ['catalog', (machine) => machine.files.set(`${app}/Contents/Info.plist`, Buffer.from(plist('<key>CFBundleIconName</key><string>AppIcon</string>')))],
        ['missing', (machine) => machine.files.set(`${app}/Contents/Info.plist`, Buffer.from(plist('<key>CFBundleIconFile</key><string>Editor</string>')))],
        [
            'oversized',
            (machine) => {
                machine.files.set(`${app}/Contents/Info.plist`, Buffer.from(plist('<key>CFBundleIconFile</key><string>Editor</string>')));
                machine.files.set(`${app}/Contents/Resources/Editor.icns`, Buffer.alloc(icnsBytesAtMost + 1));
            },
        ],
        [
            'undecodable',
            (machine) => {
                machine.files.set(`${app}/Contents/Info.plist`, Buffer.from(plist('<key>CFBundleIconFile</key><string>Editor</string>')));
                machine.files.set(`${app}/Contents/Resources/Editor.icns`, icns(['ic08', png(8)]));
            },
        ],
    ];
    for (const [name, plant] of cases) {
        const machine = planted('darwin', name === 'undecodable' ? { fromBuffer: () => image(0, 0, true) } : {});
        machine.times.set(app, 1);
        plant(machine);
        assert.equal(await new IconReader(machine.deps).icon(app), null, name);
        if (name === 'oversized') assert.ok(!machine.calls.includes(`read ${app}/Contents/Resources/Editor.icns`), 'an oversized set is not read');
    }
});

const listing = Buffer.from(plist('<key>CFBundleIconFile</key><string>Editor</string>'));

test('on macOS a property list or an icon set that is no regular file is never read, and one past its cap is not read', async () => {
    const cases: [string, string, (machine: Planted) => void][] = [
        ['a list that is a device', `${app}/Contents/Info.plist`, (machine) => {
            machine.files.set(`${app}/Contents/Info.plist`, listing);
            machine.special.add(`${app}/Contents/Info.plist`);
        }],
        ['a set that is a pipe', `${app}/Contents/Resources/Editor.icns`, (machine) => {
            machine.files.set(`${app}/Contents/Info.plist`, listing);
            machine.files.set(`${app}/Contents/Resources/Editor.icns`, icns(['ic08', png(8)]));
            machine.special.add(`${app}/Contents/Resources/Editor.icns`);
        }],
        ['a list past its own cap', `${app}/Contents/Info.plist`, (machine) => {
            machine.files.set(`${app}/Contents/Info.plist`, Buffer.concat([listing, Buffer.alloc(plistBytesAtMost)]));
        }],
    ];
    for (const [name, unread, plant] of cases) {
        const machine = planted('darwin');
        machine.times.set(app, 1);
        plant(machine);
        assert.equal(await new IconReader(machine.deps).icon(app), null, name);
        assert.ok(!machine.calls.includes(`read ${unread}`), `${name} is not read`);
        assert.ok(!machine.calls.some((call) => call.startsWith('fromBuffer')), `${name}: nothing decoded`);
    }
});

test('on macOS an icon set that grows past its cap between the look and the read gives no icon', async () => {
    const machine = planted('darwin');
    machine.times.set(app, 1);
    machine.files.set(`${app}/Contents/Info.plist`, listing);
    machine.files.set(`${app}/Contents/Resources/Editor.icns`, icns(['ic08', png(8)]));
    machine.growing.add(`${app}/Contents/Resources/Editor.icns`);
    assert.equal(await new IconReader(machine.deps).icon(app), null);
    assert.ok(!machine.calls.some((call) => call.startsWith('fromBuffer')));
});

test('an answer of no icon is kept: a list with no icon file is read once a run', async () => {
    const machine = planted('darwin');
    machine.times.set(app, 1);
    machine.files.set(`${app}/Contents/Info.plist`, Buffer.from(plist('<key>CFBundleIconName</key><string>AppIcon</string>')));
    const reader = new IconReader(machine.deps);
    assert.equal(await reader.icon(app), null);
    assert.equal(await reader.icon(app), null);
    assert.equal(machine.calls.filter((call) => call === `read ${app}/Contents/Info.plist`).length, 1);
});

test('a chunk claiming more than 1024 px a side is skipped, and a set holding only such a chunk decodes nothing', async () => {
    const claimed = png(4);
    claimed.writeUInt32BE(40000, 16);
    claimed.writeUInt32BE(40000, 20);
    assert.equal(largestPng(icns(['ic10', claimed])), null);
    const wide = png(4);
    wide.writeUInt32BE(edgeAtMost + 1, 16);
    const small = png(8);
    assert.deepEqual(largestPng(icns(['ic10', wide], ['ic07', small])), small, 'one side over is enough');
    const machine = planted('darwin');
    machine.times.set(app, 1);
    machine.files.set(`${app}/Contents/Info.plist`, listing);
    machine.files.set(`${app}/Contents/Resources/Editor.icns`, icns(['ic10', claimed]));
    assert.equal(await new IconReader(machine.deps).icon(app), null);
    assert.ok(!machine.calls.some((call) => call.startsWith('fromBuffer')), 'nothing handed to the decoder');
});

test('a file is read through one handle only when regular, and never past its cap', async () => {
    const folder = mkdtempSync(join(tmpdir(), 'jason-open-in-icons-'));
    try {
        const file = join(folder, 'set.icns');
        writeFileSync(file, Buffer.alloc(10, 7));
        assert.deepEqual(await readRegularFileAtMost(file, 10), Buffer.alloc(10, 7), 'a file at its cap');
        assert.equal(await readRegularFileAtMost(file, 9), null, 'a file past its cap at the read');
        const inner = join(folder, 'Resources');
        mkdirSync(inner);
        assert.equal(await readRegularFileAtMost(inner, 10), null, 'a folder is no regular file');
        assert.equal(await readRegularFileAtMost(join(folder, 'gone'), 10), null);
        writeFileSync(join(folder, 'empty'), '');
        assert.deepEqual(await readRegularFileAtMost(join(folder, 'empty'), 10), Buffer.alloc(0));
    } finally {
        rmSync(folder, { recursive: true, force: true });
    }
});

test('a device is no regular file: the read of one gives nothing and ends', { skip: process.platform === 'win32' }, async () => {
    assert.equal(await readRegularFileAtMost('/dev/zero', 10), null);
});

test('a file that says it is empty and is not gives nothing: the byte past its size is read', { skip: process.platform !== 'linux' }, async () => {
    // A regular file of the system's that claims no size and has content.
    assert.equal(await readRegularFileAtMost('/proc/self/status', 4096), null);
});

// ---- The places that carry an icon ----------------------------------------------------------------------------------

function probes(platform: NodeJS.Platform, present: string[], env: Record<string, string> = {}): Probes {
    return { platform, env, home: platform === 'win32' ? 'C:\\Users\\ada' : '/home/ada', onPath: () => null, exists: (path) => present.includes(path) };
}

const windows = probes('win32', ['C:\\Users\\ada\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe'], { LOCALAPPDATA: 'C:\\Users\\ada\\AppData\\Local', SystemRoot: 'C:\\Windows' });
const mac = probes('darwin', ['/Applications/Visual Studio Code.app', '/Applications/Zed.app']);
const linux = { ...probes('linux', []), onPath: (command: string) => (command === 'code' ? '/usr/bin/code' : command === 'xdg-open' ? '/usr/bin/xdg-open' : null) };

test('an icon is read for an editor’s program and File Explorer on Windows, an editor, Finder and Terminal on macOS, and nothing on Linux', () => {
    assert.equal(iconPath('vscode', windows), 'C:\\Users\\ada\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe');
    assert.equal(iconPath('files', windows), 'C:\\Windows\\explorer.exe');
    for (const id of ['claude', 'codex', 'terminal', 'browser', 'copy', 'cursor'] as const) assert.equal(iconPath(id, windows), null, id);
    assert.equal(iconPath('vscode', mac), '/Applications/Visual Studio Code.app');
    assert.equal(iconPath('files', mac), finderApp);
    assert.equal(iconPath('terminal', mac), terminalApp);
    for (const id of ['claude', 'codex', 'browser', 'copy'] as const) assert.equal(iconPath(id, mac), null, id);
    for (const id of ['vscode', 'files', 'terminal'] as const) assert.equal(iconPath(id, linux), null, id);
});

function openIn(probesOf: Probes, icon: OpenInDeps['icon']): OpenIn {
    return new OpenIn({
        probes: probesOf,
        hosts: () => ({ claude: true, codex: true }),
        cli: { run: async () => ({ exit: 0, stdout: '{}', stderr: '' }) },
        harnessRoot: () => (probesOf.platform === 'win32' ? 'C:\\Users\\ada\\Jason' : '/home/ada/Jason'),
        isDirectory: () => true,
        remembered: () => null,
        remember: () => {},
        start: async () => null,
        copy: () => {},
        terminalFlags: () => [],
        continuable: () => null,
        chatFolder: () => '/chat',
        continueFlags: () => [],
        ...(icon === undefined ? {} : { icon }),
    });
}

const pixels: PlaceIcon = { width: 1, height: 1, rgba: 'AAAAAA==' };

test('the places carry the icons read, as pixels, and the answer names no path or file', async () => {
    const asked: string[] = [];
    const answer = await openIn(windows, async (path) => {
        asked.push(path);
        return pixels;
    }).targets();
    assert.ok(!('refused' in answer));
    assert.deepEqual(asked.sort(), ['C:\\Users\\ada\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe', 'C:\\Windows\\explorer.exe']);
    assert.deepEqual(
        answer.places.map((place) => [place.id, place.icon !== undefined]),
        [['claude', false], ['codex', false], ['terminal', false], ['vscode', true], ['files', true], ['copy', false]],
    );
    const said = JSON.stringify(answer);
    assert.doesNotMatch(said, /\\\\|Code\.exe|explorer|Programs|\/Applications/);
});

test('on macOS the icons are asked of the applications, Finder and Terminal; on Linux none is asked', async () => {
    const asked: string[] = [];
    await openIn(mac, async (path) => {
        asked.push(path);
        return null;
    }).targets();
    assert.deepEqual(asked.sort(), ['/Applications/Visual Studio Code.app', '/Applications/Zed.app', finderApp, terminalApp].sort());
    const none: string[] = [];
    const answer = await openIn(linux, async (path) => {
        none.push(path);
        return pixels;
    }).targets();
    assert.deepEqual(none, []);
    assert.ok(!('refused' in answer) && answer.places.every((place) => place.icon === undefined));
});

test('a read that fails or throws leaves the glyph, and the answer still arrives', async () => {
    for (const icon of [async () => Promise.reject(new Error('gone')), () => { throw new Error('at once'); }] as OpenInDeps['icon'][]) {
        const answer = await openIn(windows, icon).targets();
        assert.ok(!('refused' in answer));
        assert.ok(answer.places.some((place) => place.id === 'vscode'));
        assert.ok(answer.places.every((place) => place.icon === undefined));
    }
    const answer = await openIn(windows, undefined).targets();
    assert.ok(!('refused' in answer) && answer.places.every((place) => place.icon === undefined), 'no reader, no icon');
});
