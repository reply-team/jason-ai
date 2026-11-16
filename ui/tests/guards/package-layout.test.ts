import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import yauzl from 'yauzl';
import { API_VERSION } from '../../src/api/version';
import {
    buildManifest,
    buildNotices,
    buildSealedPage,
    packageUi,
    sealedCarries,
    sealedPage,
} from '../../scripts/package-lib.mjs';
import config from '../../vite.config';

const ui = join(import.meta.dirname, '..', '..');
const desktopDemo = join(ui, '..', 'desktop', 'demo');
const sha256 = (bytes: Buffer | Uint8Array) => createHash('sha256').update(bytes).digest('hex');

/** The packages the UI declares as its dependencies, which the notices of every build must name. */
const dependencies = Object.keys(
    (JSON.parse(readFileSync(join(ui, 'package.json'), 'utf8')) as { dependencies: Record<string, string> })
        .dependencies,
);

/** Notices as the build writes them, a section for each of these packages. */
const notices = (named: string[] = dependencies) =>
    named.map((name) => `## ${name} 1.0.0 — MIT\n\nThe licence of ${name}.\n`).join('\n');

function entries(zip: string): Promise<Map<string, string>> {
    return new Promise((resolve, reject) => {
        const found = new Map<string, string>();
        yauzl.open(zip, { lazyEntries: true }, (error, file) => {
            if (error || !file) return reject(error);
            file.on('entry', (entry) => {
                file.openReadStream(entry, (streamError, stream) => {
                    if (streamError || !stream) return reject(streamError);
                    const chunks: Buffer[] = [];
                    stream.on('data', (c: Buffer) => chunks.push(c));
                    stream.on('end', () => {
                        found.set(entry.fileName, Buffer.concat(chunks).toString('utf8'));
                        file.readEntry();
                    });
                });
            });
            file.on('end', () => resolve(found));
            file.readEntry();
        });
    });
}

function names(zip: string): Promise<string[]> {
    return new Promise((resolve, reject) => {
        const found: string[] = [];
        yauzl.open(zip, { lazyEntries: true }, (error, file) => {
            if (error || !file) return reject(error);
            file.on('entry', (entry) => {
                found.push(entry.fileName);
                file.readEntry();
            });
            file.on('end', () => resolve(found));
            file.readEntry();
        });
    });
}

function bytesOf(zip: string, wanted: string): Promise<Buffer | null> {
    return new Promise((resolve, reject) => {
        let found: Buffer | null = null;
        yauzl.open(zip, { lazyEntries: true }, (error, file) => {
            if (error || !file) return reject(error);
            file.on('entry', (entry) => {
                if (entry.fileName !== wanted) return file.readEntry();
                file.openReadStream(entry, (streamError, stream) => {
                    if (streamError || !stream) return reject(streamError);
                    const chunks: Buffer[] = [];
                    stream.on('data', (c: Buffer) => chunks.push(c));
                    stream.on('end', () => {
                        found = Buffer.concat(chunks);
                        file.readEntry();
                    });
                });
            });
            file.on('end', () => resolve(found));
            file.readEntry();
        });
    });
}

/** A build in a fresh directory: an index, its notices, and the files named, each with its contents. */
function aBuild(files: Record<string, string | Buffer> = {}): { dir: string; dist: string } {
    const dir = mkdtempSync(join(tmpdir(), 'jason-ui-pack-'));
    const dist = join(dir, 'dist');
    mkdirSync(dist, { recursive: true });
    writeFileSync(join(dist, 'index.html'), '<title>x</title>');
    writeFileSync(join(dist, 'THIRD-PARTY-NOTICES'), notices());
    for (const [name, contents] of Object.entries(files)) {
        mkdirSync(join(dist, name, '..'), { recursive: true });
        writeFileSync(join(dist, name), contents);
    }
    return { dir, dist };
}

describe('the asset', () => {
    it("holds the build at its root, a manifest with this API version, and the build's notices", async () => {
        const dir = mkdtempSync(join(tmpdir(), 'jason-ui-pack-'));
        const dist = join(dir, 'dist');
        mkdirSync(join(dist, 'assets'), { recursive: true });
        writeFileSync(join(dist, 'index.html'), '<title>x</title>');
        writeFileSync(join(dist, 'favicon.svg'), '<svg/>');
        writeFileSync(join(dist, 'assets', 'index-abc.js'), '1');
        writeFileSync(join(dist, 'THIRD-PARTY-NOTICES'), notices());
        const out = join(dir, 'jason-ui.zip');

        await packageUi(dist, out, { version: '0.10.0', apiVersion: API_VERSION });

        const found = await entries(out);
        expect([...found.keys()].sort()).toEqual([
            'THIRD-PARTY-NOTICES',
            'assets/index-abc.js',
            'favicon.svg',
            'index.html',
            'manifest.json',
        ]);
        expect(JSON.parse(found.get('manifest.json')!)).toEqual({ version: '0.10.0', api_version: 'v1' });
        expect(found.get('THIRD-PARTY-NOTICES')).toBe(notices());
    });

    it('carries the manifest it writes, not the one a build left in dist', async () => {
        const dir = mkdtempSync(join(tmpdir(), 'jason-ui-pack-'));
        const dist = join(dir, 'dist');
        mkdirSync(join(dist, 'assets'), { recursive: true });
        writeFileSync(join(dist, 'index.html'), '<title>x</title>');
        writeFileSync(join(dist, 'manifest.json'), '{"version":"0.0.0-local","api_version":"v1"}\n');
        writeFileSync(join(dist, 'THIRD-PARTY-NOTICES'), notices());
        const out = join(dir, 'jason-ui.zip');

        await packageUi(dist, out, { version: '0.13.0', apiVersion: API_VERSION });

        expect((await names(out)).filter((name) => name === 'manifest.json')).toHaveLength(1);
        expect(JSON.parse((await entries(out)).get('manifest.json')!)).toEqual({
            version: '0.13.0',
            api_version: 'v1',
        });
    });

    it('refuses a dist that is not a build', async () => {
        const dir = mkdtempSync(join(tmpdir(), 'jason-ui-pack-'));
        const dist = join(dir, 'dist');
        mkdirSync(join(dist, 'nested'), { recursive: true });
        writeFileSync(join(dist, 'nested', 'x.js'), '1');
        await expect(packageUi(dist, join(dir, 'out.zip'), { version: '1', apiVersion: 'v1' })).rejects.toThrow(
            /index\.html/,
        );
        writeFileSync(join(dist, 'index.html'), 'x');
        await expect(packageUi(dist, join(dir, 'out.zip'), { version: '1', apiVersion: 'v1' })).rejects.toThrow(
            /nested/,
        );
    });

    it('carries the pages under sealed/, byte for byte', async () => {
        const page = readFileSync(join(desktopDemo, 'index.html'));
        const { dir, dist } = aBuild({ 'sealed/demo.html': page });
        const out = join(dir, 'jason-ui.zip');

        await packageUi(dist, out, { version: '1', apiVersion: 'v1' });

        expect(await names(out)).toContain('sealed/demo.html');
        expect(sha256((await bytesOf(out, 'sealed/demo.html'))!)).toBe(sha256(page));
    });

    it('refuses under sealed/ anything but a page, and a directory below it', async () => {
        for (const [name, refusal] of [
            ['sealed/demo.js', /sealed\/demo\.js.*\.html/],
            ['sealed/demo.css', /sealed\/demo\.css.*\.html/],
            ['sealed/more/demo.html', /sealed\/more/],
            ['Sealed/demo.html', /Sealed/],
        ] as const) {
            const { dir, dist } = aBuild({ [name]: 'x' });
            await expect(
                packageUi(dist, join(dir, 'out.zip'), { version: '1', apiVersion: 'v1' }),
                name,
            ).rejects.toThrow(refusal);
        }
    });

    it('refuses a build without its notices', async () => {
        const dir = mkdtempSync(join(tmpdir(), 'jason-ui-pack-'));
        const dist = join(dir, 'dist');
        mkdirSync(dist, { recursive: true });
        writeFileSync(join(dist, 'index.html'), 'x');
        await expect(packageUi(dist, join(dir, 'out.zip'), { version: '1', apiVersion: 'v1' })).rejects.toThrow(
            /THIRD-PARTY-NOTICES/,
        );
    });

    it('refuses notices that leave out a package the UI declares as a dependency, naming it', async () => {
        const dir = mkdtempSync(join(tmpdir(), 'jason-ui-pack-'));
        const dist = join(dir, 'dist');
        mkdirSync(dist, { recursive: true });
        writeFileSync(join(dist, 'index.html'), 'x');
        expect(dependencies).toContain('@fontsource-variable/inter');
        writeFileSync(
            join(dist, 'THIRD-PARTY-NOTICES'),
            notices(dependencies.filter((name) => name !== '@fontsource-variable/inter')),
        );
        await expect(packageUi(dist, join(dir, 'out.zip'), { version: '1', apiVersion: 'v1' })).rejects.toThrow(
            '@fontsource-variable/inter',
        );
    });
});

describe('the build', () => {
    it('writes the manifest the runtime serves a directory by, so dist/ is a build on its own', () => {
        const emitted: unknown[] = [];
        const plugin = buildManifest({ version: '0.13.0-dev', apiVersion: API_VERSION });

        (plugin.generateBundle as (this: { emitFile: (file: unknown) => void }) => void).call({
            emitFile: (file) => emitted.push(file),
        });

        expect(plugin.apply).toBe('build');
        expect(emitted).toEqual([
            { type: 'asset', fileName: 'manifest.json', source: '{"version":"0.13.0-dev","api_version":"v1"}\n' },
        ]);
    });

    it('writes the notices of exactly the packages its bundle carries, each with its own licence', () => {
        const emitted: { fileName: string; source: string }[] = [];
        const plugin = buildNotices();
        const module = (...path: string[]) => join(ui, 'node_modules', ...path);
        // A bundle as the build hands it over: the modules each chunk was made of, and the files each asset came from.
        const bundle = {
            'assets/index-abc.js': {
                type: 'chunk',
                moduleIds: [
                    join(ui, 'src', 'main.tsx'),
                    '\0vite/modulepreload-polyfill.js',
                    module('react', 'cjs', 'react.production.min.js'),
                    module('react', 'jsx-runtime.js'),
                    module('react-dom', 'client.js'),
                    module('scheduler', 'index.js'),
                    module('@preact', 'signals-react', 'runtime', 'dist', 'runtime.module.js'),
                    module('@preact', 'signals-core', 'dist', 'signals-core.module.js'),
                    module('use-sync-external-store', 'shim', 'index.js'),
                ],
            },
            'assets/index-abc.css': { type: 'asset', originalFileNames: ['index.html'] },
            'assets/inter-latin-abc.woff2': {
                type: 'asset',
                originalFileNames: ['node_modules/@fontsource-variable/inter/files/inter-latin-wght-normal.woff2'],
            },
        };

        (plugin.generateBundle as (this: { emitFile: (file: unknown) => void }, o: unknown, b: unknown) => void).call(
            { emitFile: (file) => emitted.push(file as { fileName: string; source: string }) },
            {},
            bundle,
        );

        expect(plugin.apply).toBe('build');
        const notices = emitted.find((file) => file.fileName === 'THIRD-PARTY-NOTICES')!.source;
        const carried = [
            '@fontsource-variable/inter',
            '@preact/signals-core',
            '@preact/signals-react',
            'react',
            'react-dom',
            'scheduler',
            'use-sync-external-store',
        ];
        const own = (name: string) =>
            JSON.parse(readFileSync(join(ui, 'node_modules', name, 'package.json'), 'utf8')) as {
                version: string;
                license: string;
            };
        expect(notices.split('\n').filter((line) => line.startsWith('## '))).toEqual(
            carried.map((name) => `## ${name} ${own(name).version} — ${own(name).license}`),
        );
        for (const name of carried) {
            expect(notices).toContain(readFileSync(join(ui, 'node_modules', name, 'LICENSE'), 'utf8').trim());
        }
        // React names loose-envify, a build-time transform, as a dependency of its own; the bundle does not carry it.
        expect(notices).not.toMatch(/loose-envify|js-tokens/);
    });

    it('refuses a carried package that has no licence file, naming the package and its directory', () => {
        const dir = mkdtempSync(join(tmpdir(), 'jason-ui-notices-'));
        const pkg = join(dir, 'node_modules', 'unlicensed');
        mkdirSync(pkg, { recursive: true });
        writeFileSync(
            join(pkg, 'package.json'),
            JSON.stringify({ name: 'unlicensed', version: '1.0.0', license: 'MIT' }),
        );
        writeFileSync(join(pkg, 'index.js'), 'export default 1;');
        const bundle = { 'assets/index-abc.js': { type: 'chunk', moduleIds: [join(pkg, 'index.js')] } };
        const plugin = buildNotices();

        const generate = () =>
            (
                plugin.generateBundle as (this: { emitFile: (file: unknown) => void }, o: unknown, b: unknown) => void
            ).call({ emitFile: () => undefined }, {}, bundle);

        expect(generate).toThrow('unlicensed');
        expect(generate).toThrow(pkg.replaceAll('\\', '/'));
    });

    it('copies the demo page the desktop shows to sealed/demo.html, byte for byte', () => {
        const emitted: { type: string; fileName: string; source: Uint8Array }[] = [];
        const plugin = buildSealedPage();

        (plugin.generateBundle as (this: { emitFile: (file: unknown) => void }) => void).call({
            emitFile: (file) => emitted.push(file as { type: string; fileName: string; source: Uint8Array }),
        });

        expect(plugin.apply).toBe('build');
        expect(sealedPage).toEqual({ from: join(desktopDemo, 'index.html'), to: 'sealed/demo.html' });
        expect(emitted.map(({ type, fileName }) => ({ type, fileName }))).toEqual([
            { type: 'asset', fileName: 'sealed/demo.html' },
        ]);
        expect(emitted[0]!.source).toBeInstanceOf(Uint8Array);
        expect(sha256(emitted[0]!.source)).toBe(sha256(readFileSync(join(desktopDemo, 'index.html'))));
    });

    it('names in its notices what the sealed page carries, each with its licence text, when the build holds it', () => {
        const generate = (bundle: Record<string, unknown>) => {
            const emitted: { fileName: string; source: string }[] = [];
            (
                buildNotices().generateBundle as (
                    this: { emitFile: (file: unknown) => void },
                    o: unknown,
                    b: unknown,
                ) => void
            ).call({ emitFile: (file) => emitted.push(file as { fileName: string; source: string }) }, {}, bundle);
            return emitted.find((file) => file.fileName === 'THIRD-PARTY-NOTICES')!.source;
        };

        const withPage = generate({ 'sealed/demo.html': { type: 'asset', originalFileNames: [] } });
        const without = generate({});

        expect(sealedCarries.length).toBeGreaterThan(0);
        for (const { name, version, licence, file } of sealedCarries) {
            expect(withPage).toContain(`### ${name} ${version} — ${licence}`);
            expect(withPage).toContain(readFileSync(join(desktopDemo, file), 'utf8').trim());
            expect(without).not.toContain(`### ${name}`);
        }
        expect(withPage).toContain('sealed/demo.html');
        // The part is its own: the headings the packager reads as the bundle's packages stay as they were.
        expect(withPage.split('\n').filter((line) => line.startsWith('## '))).toEqual(
            without.split('\n').filter((line) => line.startsWith('## ')),
        );
    });

    it('names what the sealed page carries as the desktop names what its demo page carries', () => {
        const script = readFileSync(join(ui, '..', 'desktop', 'scripts', 'notices.mjs'), 'utf8');
        const list = /const demoCarries = \[(?<body>[^\]]*)\]/.exec(script)?.groups?.body ?? '';
        const desktop = [...list.matchAll(/\{(?<entry>[^}]*)\}/g)].map(({ groups }) => {
            const field = (key: string) => new RegExp(`${key}: '([^']*)'`).exec(groups!.entry)?.[1];
            return { name: field('name'), version: field('version'), licence: field('licence'), file: field('file') };
        });
        expect(desktop.length).toBeGreaterThan(0);
        expect(sealedCarries).toEqual(desktop);
    });

    it('is a step of the build the Vite configuration runs', () => {
        const plugins = (config.plugins ?? []).flat() as { name?: string }[];
        expect(plugins.map((plugin) => plugin?.name)).toContain('jason-ui-manifest');
        expect(plugins.map((plugin) => plugin?.name)).toContain('jason-ui-notices');
        expect(plugins.map((plugin) => plugin?.name)).toContain('jason-ui-sealed-page');
    });
});
