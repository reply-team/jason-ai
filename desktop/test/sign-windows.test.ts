// How scripts/sign-windows.mjs signs a Windows file: one signtool run through the Trusted Signing client, with a SHA-256
// digest and an RFC 3161 timestamp, and the bundled jason.exe left with the signature its own job gave it. signtool is
// never started here: these rows hold the run each file is given and the files it refuses to sign.

import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, win32 } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { carriesSignature, signRun, signs } from '../scripts/sign-windows.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const at = (...parts: string[]) => win32.join('D:', 'a', ...parts);
const env = {
    JASON_SIGNTOOL: win32.join('C:', 'kits', 'signtool.exe'),
    JASON_SIGNING_DLIB: win32.join('C:', 't', 'Azure.CodeSigning.Dlib.dll'),
    JASON_SIGNING_METADATA: win32.join('C:', 't', 'metadata.json'),
};

test('a file is signed by signtool through the Trusted Signing client, SHA-256 and timestamped under RFC 3161', () => {
    const file = at('dist', 'win-unpacked', 'Jason.exe');
    assert.deepEqual(signRun(file, env), {
        program: env.JASON_SIGNTOOL,
        args: ['sign', '/v', '/fd', 'SHA256', '/tr', 'http://timestamp.acs.microsoft.com', '/td', 'SHA256',
            '/dlib', env.JASON_SIGNING_DLIB, '/dmdf', env.JASON_SIGNING_METADATA, file],
    });
});

test('the bundled jason.exe keeps the signature its own job gave it; every other file is signed', () => {
    for (const file of [at('dist', 'win-unpacked', 'resources', 'bundle', 'jason.exe'), 'D:/a/dist/win-unpacked/resources/bundle/JASON.EXE']) {
        assert.equal(signs(file), false, file);
        assert.equal(signRun(file, env), null, file);
    }
    for (const file of [at('dist', 'win-unpacked', 'Jason.exe'), at('dist', 'jason-desktop-windows-x64-setup.exe'),
        at('dist', 'jason-desktop-windows-x64-setup.__uninstaller.exe'), at('dist', 'win-unpacked', 'resources', 'jason.exe')]) {
        assert.equal(signs(file), true, file);
    }
});

/**
 * A made-up program image's headers, up to its data directories: a PE32 or PE32+ optional header whose certificate table
 * holds `certificate` bytes, which is how an image carries an Authenticode signature of its own.
 */
function image(magic: 0x10b | 0x20b, certificate: number): Buffer {
    const pe = 0x80;
    const optional = pe + 24;
    const directories = optional + (magic === 0x20b ? 112 : 96);
    const bytes = Buffer.alloc(directories + 16 * 8);
    bytes.write('MZ', 0, 'latin1');
    bytes.writeUInt32LE(pe, 0x3c);
    bytes.write('PE\0\0', pe, 'latin1');
    bytes.writeUInt16LE(magic, optional);
    bytes.writeUInt32LE(16, directories - 4);
    bytes.writeUInt32LE(certificate === 0 ? 0 : bytes.length, directories + 4 * 8);
    bytes.writeUInt32LE(certificate, directories + 4 * 8 + 4);
    return bytes;
}

test('a library that came signed keeps its own signature: one whose image carries a certificate table is not signed again', (t) => {
    const folder = mkdtempSync(join(tmpdir(), 'jason-desktop-sign-'));
    t.after(() => rmSync(folder, { recursive: true, force: true }));
    const planted = (name: string, bytes: Buffer | string) => {
        const file = join(folder, name);
        writeFileSync(file, bytes);
        return file;
    };

    for (const [name, magic] of [['d3dcompiler_47.dll', 0x20b], ['old-style.dll', 0x10b]] as const) {
        const file = planted(name, image(magic, 0x2a00));
        assert.equal(carriesSignature(file), true, name);
        assert.equal(signRun(file, env), null, name);
    }
    for (const [name, bytes] of [['ffmpeg.dll', image(0x20b, 0)], ['Jason.exe', image(0x10b, 0)], ['no-image.dll', 'made up'],
        ['cut-short.dll', image(0x20b, 0x2a00).subarray(0, 0x100)]] as const) {
        const file = planted(name, bytes);
        assert.equal(carriesSignature(file), false, name);
        assert.equal(signRun(file, env)?.args.at(-1), file, name);
    }
    // A file that is not there carries nothing: signtool is asked, and its refusal fails the package.
    assert.equal(carriesSignature(join(folder, 'missing.dll')), false);
});

test('a run without signtool, the client or its metadata is refused by the name it lacks, never by a value', () => {
    for (const name of Object.keys(env)) {
        const lacking = { ...env, [name]: '' };
        assert.throws(() => signRun(at('Jason.exe'), lacking), new RegExp(`${name} is not set`), name);
    }
});

test('the signing module prints nothing and starts signtool alone, without a shell', () => {
    const text = readFileSync(join(root, 'scripts', 'sign-windows.mjs'), 'utf8');
    assert.doesNotMatch(text, /\bconsole\s*\./);
    assert.doesNotMatch(text, /process\.(?:stdout|stderr)/);
    assert.match(text, /await signtool\(run\.program, run\.args, \{ windowsHide: true \}\);/);
    assert.doesNotMatch(text, /\bshell\s*:/);
});
