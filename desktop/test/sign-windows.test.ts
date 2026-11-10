// How scripts/sign-windows.mjs signs a Windows file: one signtool run through the Trusted Signing client, with a SHA-256
// digest and an RFC 3161 timestamp, and the bundled jason.exe left with the signature its own job gave it. signtool is
// never started here: these rows hold the run each file is given and the files it refuses to sign.

import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join, win32 } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { signRun, signs } from '../scripts/sign-windows.mjs';

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
