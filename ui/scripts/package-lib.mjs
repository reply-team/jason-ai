import { createWriteStream, existsSync, mkdirSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import yazl from 'yazl';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');

/** The layout's directory for content the UI does not trust (`UiAssetLayout.SealedDirectory`): pages only, none below it. */
const sealedDirectory = 'sealed';

/**
 * The demo page the desktop shows in Demo, which the build carries too: one source, `desktop/demo/index.html`, copied
 * byte for byte into the layout's sealed directory, where the runtime serves it unable to act.
 */
export const sealedPage = {
    from: join(root, '..', 'desktop', 'demo', 'index.html'),
    to: `${sealedDirectory}/demo.html`,
};

/**
 * What the demo page carries inside it, as the desktop names it (`desktop/scripts/notices.mjs`, `demoCarries`): no
 * lockfile names these, so they are named here, each with its licence text beside the page.
 */
export const sealedCarries = [
    { name: '@fontsource-variable/inter', version: '5.3.0', licence: 'OFL-1.1', file: 'LICENSE-Inter.txt' },
];

/** Every file under dist as a forward-slashed relative path, checked the way the runtime's unpacker checks an archive. */
function buildFiles(dist) {
    if (!existsSync(join(dist, 'index.html'))) throw new Error(`${dist} holds no index.html, so it is not a build`);
    const files = [];
    const walk = (dir) => {
        for (const name of readdirSync(dir)) {
            if (name.startsWith('.')) throw new Error(`${join(dir, name)} is hidden; a build has no hidden files`);
            const path = join(dir, name);
            const rel = relative(dist, path).split('\\').join('/');
            // The build's own manifest serves dist/ as it stands; the asset carries the one written below, with the
            // version it is published as.
            if (rel === 'manifest.json') continue;
            if (statSync(path).isDirectory()) {
                if (rel !== 'assets' && rel !== sealedDirectory) {
                    throw new Error(
                        `${rel}/ is a directory other than assets/ and ${sealedDirectory}/; a build keeps its files at the root, under assets/ and under ${sealedDirectory}/`,
                    );
                }
                walk(path);
            } else {
                // Under sealed/ a build holds pages alone, as the runtime serves and the CLI installs them
                // (`UiAssetLayout.AdmitsKind`).
                if (rel.startsWith(`${sealedDirectory}/`) && !rel.endsWith('.html')) {
                    throw new Error(
                        `${rel} is not a page; a build holds under ${sealedDirectory}/ only files ending in .html`,
                    );
                }
                files.push(rel);
            }
        }
    };
    walk(dist);
    return files.sort();
}

/**
 * The package directory a file of the build came from, or null for the project's own files and the bundler's virtual
 * modules: the folder after the last node_modules of its path, two segments for a scoped name.
 */
function packageDirOf(id) {
    const path = resolve(root, id.replace(/^\0/, '').replace(/\?.*$/, '')).split('\\').join('/');
    const parts = path.split('/');
    const at = parts.lastIndexOf('node_modules');
    if (at < 0 || at + 1 >= parts.length) return null;
    const length = parts[at + 1].startsWith('@') ? 2 : 1;
    return parts.slice(0, at + 1 + length).join('/');
}

/** The packages the bundle carries, each with its licence text read from its own package. */
function thirdPartyNotices(bundle) {
    const dirs = new Set();
    for (const output of Object.values(bundle)) {
        const ids = output.type === 'chunk' ? output.moduleIds : (output.originalFileNames ?? []);
        for (const id of ids) {
            const dir = packageDirOf(id);
            if (dir) dirs.add(dir);
        }
    }
    const packages = [...dirs].map((dir) => {
        const pkg = JSON.parse(readFileSync(join(dir, 'package.json'), 'utf8'));
        const licenseFile = readdirSync(dir).find((f) => /^(LICENSE|LICENCE)(\..*)?$/i.test(f));
        // The licence travels with the code: a notice without its text would leave out the copyright line it requires.
        if (!licenseFile) {
            throw new Error(
                `${pkg.name} is in the bundle, but ${dir} holds no LICENSE or LICENCE file to carry with it`,
            );
        }
        return {
            name: pkg.name,
            version: pkg.version,
            license: pkg.license ?? 'see text',
            text: readFileSync(join(dir, licenseFile), 'utf8'),
        };
    });
    const lines = ['Third-party notices for the Jason UI build.', ''];
    for (const { name, version, license, text } of packages.sort(
        (a, b) => a.name.localeCompare(b.name) || a.version.localeCompare(b.version),
    )) {
        lines.push(`## ${name} ${version} — ${license}`, '', text.trim(), '');
    }
    if (sealedPage.to in bundle) {
        lines.push(
            `# What ${sealedPage.to} carries`,
            '',
            `The demo page under ${sealedDirectory}/ is one static file with its script, styles and fonts inline. Besides`,
            'Jason’s own code it carries, each under the licence named here:',
            '',
        );
        for (const { name, version, licence, file } of sealedCarries) {
            const text = readFileSync(join(dirname(sealedPage.from), file), 'utf8');
            lines.push(`### ${name} ${version} — ${licence}`, '', text.trim(), '');
        }
    }
    return lines.join('\n');
}

/** What a build says it is: the version, and the API it was built for. The runtime serves no directory without one. */
export function manifestOf(version, apiVersion) {
    return JSON.stringify({ version, api_version: apiVersion }) + '\n';
}

/**
 * The build writes its manifest into dist/, so a build is served as it stands, linked or copied into ui/custom/,
 * with no package in between.
 */
export function buildManifest({ version, apiVersion }) {
    return {
        name: 'jason-ui-manifest',
        apply: 'build',
        generateBundle() {
            this.emitFile({ type: 'asset', fileName: 'manifest.json', source: manifestOf(version, apiVersion) });
        },
    };
}

/**
 * The build writes the notices of exactly the packages its bundle carries, read from the modules each chunk was made
 * of and the files each asset came from; a package that is a dependency but is not in the bundle is not named.
 */
export function buildNotices() {
    return {
        name: 'jason-ui-notices',
        apply: 'build',
        enforce: 'post',
        generateBundle(_options, bundle) {
            this.emitFile({ type: 'asset', fileName: 'THIRD-PARTY-NOTICES', source: thirdPartyNotices(bundle) });
        },
    };
}

/** The build carries the desktop's demo page as it is, its bytes untouched, under the layout's sealed directory. */
export function buildSealedPage() {
    return {
        name: 'jason-ui-sealed-page',
        apply: 'build',
        generateBundle() {
            this.emitFile({
                type: 'asset',
                fileName: sealedPage.to,
                source: new Uint8Array(readFileSync(sealedPage.from)),
            });
        },
    };
}

export function packageUi(dist, out, { version, apiVersion }) {
    return new Promise((resolve, reject) => {
        let files;
        try {
            files = buildFiles(dist);
            if (!files.includes('THIRD-PARTY-NOTICES')) {
                throw new Error(`${dist} holds no THIRD-PARTY-NOTICES, which the build writes beside index.html`);
            }
            // Every package the UI declares as a dependency is carried by the build, so its notices name each one, and
            // notices that leave one out are refused here rather than shipped.
            const headings = readFileSync(join(dist, 'THIRD-PARTY-NOTICES'), 'utf8').split('\n');
            const { dependencies = {} } = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));
            const missing = Object.keys(dependencies).filter(
                (name) => !headings.some((line) => line.startsWith(`## ${name} `)),
            );
            if (missing.length > 0) {
                throw new Error(`${dist}/THIRD-PARTY-NOTICES names no ${missing.join(', ')}, which the UI depends on`);
            }
        } catch (error) {
            reject(error);
            return;
        }
        mkdirSync(dirname(out), { recursive: true });
        const zip = new yazl.ZipFile();
        for (const file of files) zip.addFile(join(dist, file), file);
        zip.addBuffer(Buffer.from(manifestOf(version, apiVersion)), 'manifest.json');
        zip.outputStream
            .pipe(createWriteStream(out))
            .on('close', () => resolve(out))
            .on('error', reject);
        zip.end();
    });
}
