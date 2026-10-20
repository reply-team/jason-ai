import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import yauzl from 'yauzl';
import { API_VERSION } from '../../src/api/version';
import { buildManifest, packageUi } from '../../scripts/package-lib.mjs';
import config from '../../vite.config';

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

describe('the asset', () => {
    it('holds the build at its root, a manifest with this API version, and the notices', async () => {
        const dir = mkdtempSync(join(tmpdir(), 'jason-ui-pack-'));
        const dist = join(dir, 'dist');
        mkdirSync(join(dist, 'assets'), { recursive: true });
        writeFileSync(join(dist, 'index.html'), '<title>x</title>');
        writeFileSync(join(dist, 'favicon.svg'), '<svg/>');
        writeFileSync(join(dist, 'assets', 'index-abc.js'), '1');
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
        const notices = found.get('THIRD-PARTY-NOTICES')!;
        for (const name of ['preact', '@preact/signals', '@preact/signals-core', '@fontsource-variable/inter']) {
            expect(notices).toContain(name);
        }
        expect(notices).toMatch(/MIT/);
        expect(notices).toMatch(/OFL|Open Font License/);
    });

    it('carries the manifest it writes, not the one a build left in dist', async () => {
        const dir = mkdtempSync(join(tmpdir(), 'jason-ui-pack-'));
        const dist = join(dir, 'dist');
        mkdirSync(join(dist, 'assets'), { recursive: true });
        writeFileSync(join(dist, 'index.html'), '<title>x</title>');
        writeFileSync(join(dist, 'manifest.json'), '{"version":"0.0.0-local","api_version":"v1"}\n');
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

    it('is a step of the build the Vite configuration runs', () => {
        const plugins = (config.plugins ?? []).flat() as { name?: string }[];
        expect(plugins.map((plugin) => plugin?.name)).toContain('jason-ui-manifest');
    });
});
