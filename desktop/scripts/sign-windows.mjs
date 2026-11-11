// electron-builder's custom sign for the Windows package, which a release names with JASON_WINDOWS_SIGN=1: each file
// electron-builder signs - Jason.exe, the libraries beside it, the installer, the uninstaller - goes through signtool and the Trusted Signing
// client, whose only credential is the Azure CLI sign-in the release job made with its OIDC token. The digest is
// SHA-256 and the timestamp RFC 3161's, from the signing service's own authority, so a signature outlives its
// certificate. The bundled jason.exe is skipped: its own job signed it, and the package carries those very bytes. So
// is a library that came signed, Microsoft's d3dcompiler_47.dll and dxil.dll among Electron's: it keeps the signature
// its maker gave it, which the release then verifies as it verifies ours.
//
// The release job names the three files the run needs, and nothing here prints: signtool's own lines are its output,
// kept from the log unless it fails, when the error carries them.

import { execFile } from 'node:child_process';
import { closeSync, openSync, readSync } from 'node:fs';
import { promisify } from 'node:util';

const signtool = promisify(execFile);

const required = ['JASON_SIGNTOOL', 'JASON_SIGNING_DLIB', 'JASON_SIGNING_METADATA'];
const bundled = /[\\/]resources[\\/]bundle[\\/]jason\.exe$/i;

/** Whether a file of the package is signed here: every one but the bundled jason.exe. */
export function signs(file) {
    return !bundled.test(file);
}

/**
 * Whether the program image `file` already carries an Authenticode signature of its own: its optional header's
 * certificate table, the fifth data directory, holds bytes. A file that is not there, or is no image, carries none.
 */
export function carriesSignature(file) {
    const header = Buffer.alloc(4096);
    let read = 0;
    try {
        const handle = openSync(file, 'r');
        try {
            read = readSync(handle, header, 0, header.length, 0);
        } finally {
            closeSync(handle);
        }
    } catch {
        return false;
    }
    const at = (offset, size) => offset >= 0 && offset + size <= read;
    if (!at(0x3c, 4) || header.toString('latin1', 0, 2) !== 'MZ') return false;
    const pe = header.readUInt32LE(0x3c);
    if (!at(pe, 26) || header.toString('latin1', pe, pe + 4) !== 'PE\0\0') return false;
    const optional = pe + 24;
    const magic = header.readUInt16LE(optional);
    if (magic !== 0x10b && magic !== 0x20b) return false;
    const directories = optional + (magic === 0x20b ? 112 : 96);
    if (!at(directories - 4, 4) || header.readUInt32LE(directories - 4) <= 4) return false;
    const certificate = directories + 4 * 8;
    return at(certificate, 8) && header.readUInt32LE(certificate + 4) > 0;
}

/** The signtool run that signs `file`, or null for a file left as it is: the bundled jason.exe, or one that came signed. */
export function signRun(file, env = process.env) {
    if (!signs(file) || carriesSignature(file)) return null;
    for (const name of required) {
        if (!env[name]) throw new Error(`${name} is not set: the release names signtool, the signing client and its metadata.`);
    }
    return {
        program: env['JASON_SIGNTOOL'],
        args: ['sign', '/v', '/fd', 'SHA256', '/tr', 'http://timestamp.acs.microsoft.com', '/td', 'SHA256',
            '/dlib', env['JASON_SIGNING_DLIB'], '/dmdf', env['JASON_SIGNING_METADATA'], file],
    };
}

/** electron-builder's custom sign: signs the file the configuration names, and fails the package if signtool does. */
export default async function sign(configuration) {
    const run = signRun(configuration.path);
    if (run === null) return;
    await signtool(run.program, run.args, { windowsHide: true });
}
