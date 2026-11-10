// electron-builder's custom sign for the Windows package, which a release names with JASON_WINDOWS_SIGN=1: each file
// electron-builder signs - Jason.exe, the installer, the uninstaller - goes through signtool and the Trusted Signing
// client, whose only credential is the Azure CLI sign-in the release job made with its OIDC token. The digest is
// SHA-256 and the timestamp RFC 3161's, from the signing service's own authority, so a signature outlives its
// certificate. The bundled jason.exe is skipped: its own job signed it, and the package carries those very bytes.
//
// The release job names the three files the run needs, and nothing here prints: signtool's own lines are its output,
// kept from the log unless it fails, when the error carries them.

import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const signtool = promisify(execFile);

const required = ['JASON_SIGNTOOL', 'JASON_SIGNING_DLIB', 'JASON_SIGNING_METADATA'];
const bundled = /[\\/]resources[\\/]bundle[\\/]jason\.exe$/i;

/** Whether a file of the package is signed here: every one but the bundled jason.exe. */
export function signs(file) {
    return !bundled.test(file);
}

/** The signtool run that signs `file`, or null for a file left as it is. */
export function signRun(file, env = process.env) {
    if (!signs(file)) return null;
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
