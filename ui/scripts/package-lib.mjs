import { createWriteStream, existsSync, mkdirSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import yazl from 'yazl';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');

/** Every file under dist as a forward-slashed relative path, checked the way the runtime's unpacker checks an archive. */
function buildFiles(dist) {
    if (!existsSync(join(dist, 'index.html'))) throw new Error(`${dist} holds no index.html, so it is not a build`);
    const files = [];
    const walk = (dir) => {
        for (const name of readdirSync(dir)) {
            if (name.startsWith('.')) throw new Error(`${join(dir, name)} is hidden; a build has no hidden files`);
            const path = join(dir, name);
            const rel = relative(dist, path).split('\\').join('/');
            if (statSync(path).isDirectory()) {
                if (rel !== 'assets') {
                    throw new Error(
                        `${rel}/ is a directory other than assets/; a build keeps its files at the root and under assets/`,
                    );
                }
                walk(path);
            } else {
                files.push(rel);
            }
        }
    };
    walk(dist);
    return files.sort();
}

/** The production dependencies, walked transitively, each with its licence text. */
function thirdPartyNotices() {
    const seen = new Map();
    const visit = (name) => {
        if (seen.has(name)) return;
        const dir = join(root, 'node_modules', name);
        const pkg = JSON.parse(readFileSync(join(dir, 'package.json'), 'utf8'));
        const licenseFile = readdirSync(dir).find((f) => /^(LICENSE|LICENCE)(\..*)?$/i.test(f));
        seen.set(name, {
            version: pkg.version,
            license: pkg.license ?? 'see text',
            text: licenseFile ? readFileSync(join(dir, licenseFile), 'utf8') : '',
        });
        for (const dep of Object.keys(pkg.dependencies ?? {})) visit(dep);
    };
    const own = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));
    for (const dep of Object.keys(own.dependencies ?? {})) visit(dep);
    const lines = ['Third-party notices for the Jason UI build.', ''];
    for (const [name, { version, license, text }] of [...seen.entries()].sort(([a], [b]) => a.localeCompare(b))) {
        lines.push(`## ${name} ${version} — ${license}`, '', text.trim(), '');
    }
    return lines.join('\n');
}

export function packageUi(dist, out, { version, apiVersion }) {
    return new Promise((resolve, reject) => {
        let files;
        try {
            files = buildFiles(dist);
        } catch (error) {
            reject(error);
            return;
        }
        mkdirSync(dirname(out), { recursive: true });
        const zip = new yazl.ZipFile();
        for (const file of files) zip.addFile(join(dist, file), file);
        zip.addBuffer(Buffer.from(JSON.stringify({ version, api_version: apiVersion }) + '\n'), 'manifest.json');
        zip.addBuffer(Buffer.from(thirdPartyNotices()), 'THIRD-PARTY-NOTICES');
        zip.outputStream
            .pipe(createWriteStream(out))
            .on('close', () => resolve(out))
            .on('error', reject);
        zip.end();
    });
}
