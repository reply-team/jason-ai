// The one file of the smoke that starts processes: a scratch runtime, the three ui verbs, and a build placed where the
// runtime serves it. Nothing here seeds data or reads the runtime's token; the suite drives the CLI as a person would.
import { execFile } from 'node:child_process';
import { cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { promisify } from 'node:util';

const run = promisify(execFile);
const ui = resolve(import.meta.dirname, '..', '..');
const repo = resolve(ui, '..');

export const dist = resolve(ui, 'dist');
export const jasonExe =
    process.env.JASON_EXE ??
    resolve(
        repo,
        'runtime',
        'src',
        'Jason.App',
        'bin',
        'Release',
        'net10.0',
        process.platform === 'win32' ? 'jason.exe' : 'jason',
    );

export type Build = 'none' | 'default' | 'custom' | 'both';

export class Jason {
    port = 0;

    private constructor(
        readonly dataDir: string,
        private readonly configuredPort: number,
    ) {}

    static async start({ build = 'default', port = 0 }: { build?: Build; port?: number } = {}): Promise<Jason> {
        const dataDir = mkdtempSync(join(tmpdir(), 'jason-ui-smoke-'));
        mkdirSync(join(dataDir, 'config'), { recursive: true });
        writeFileSync(
            join(dataDir, 'config', 'settings.json'),
            JSON.stringify({ Dispatcher: { Enabled: false }, Runtime: { Port: port } }),
        );
        const jason = new Jason(dataDir, port);
        if (build === 'default' || build === 'both') jason.placeBuild('default', 'default-smoke');
        if (build === 'custom' || build === 'both') jason.placeBuild('custom', 'custom-smoke');
        await jason.up();
        return jason;
    }

    /** Copies the build into <data>/ui/<kind>/ with a manifest naming the marker as its version, so System tells the two apart. */
    placeBuild(kind: 'default' | 'custom', marker = '0.0.0-smoke'): void {
        const target = join(this.dataDir, 'ui', kind);
        rmSync(target, { recursive: true, force: true });
        cpSync(dist, target, { recursive: true });
        writeFileSync(join(target, 'manifest.json'), JSON.stringify({ version: marker, api_version: 'v1' }));
    }

    removeBuild(kind: 'default' | 'custom'): void {
        rmSync(join(this.dataDir, 'ui', kind), { recursive: true, force: true });
    }

    get origin(): string {
        return `http://127.0.0.1:${this.port}`;
    }

    async uiOpen(...args: string[]): Promise<{ url: string; expires_at: string }> {
        const { stdout } = await this.jason('ui', 'open', '--no-open', ...args);
        return JSON.parse(stdout) as { url: string; expires_at: string };
    }

    async uiRevoke(idOrAll: string): Promise<void> {
        await this.jason('ui', 'revoke', idOrAll);
    }

    async uiTokens(): Promise<{ items: { id: string }[] }> {
        const { stdout } = await this.jason('ui', 'tokens');
        return JSON.parse(stdout) as { items: { id: string }[] };
    }

    async restart(): Promise<void> {
        await this.stop();
        await this.up();
    }

    async stop(): Promise<void> {
        try {
            await this.jason('runtime', 'stop');
        } catch {
            // Already gone.
        }
    }

    /** Removes the scratch data directory; call after stop(). */
    dispose(): void {
        rmSync(this.dataDir, { recursive: true, force: true });
    }

    private async up(): Promise<void> {
        await this.jason('runtime', 'start');
        const descriptor = JSON.parse(readFileSync(join(this.dataDir, 'run', 'runtime.json'), 'utf8')) as {
            base_url: string;
        };
        this.port = Number(new URL(descriptor.base_url).port);
        if (this.configuredPort !== 0 && this.port !== this.configuredPort) {
            throw new Error(`asked for port ${this.configuredPort}, got ${this.port}`);
        }
    }

    /** The CLI as a person runs it. A non-zero exit is an error carrying stdout, which is the runtime's envelope. */
    private async jason(...args: string[]): Promise<{ stdout: string; stderr: string }> {
        try {
            return await run(jasonExe, args, {
                env: { ...process.env, JASON_DATA_DIR: this.dataDir, JASON_Update__CheckEnabled: 'false' },
                timeout: 30_000,
            });
        } catch (error) {
            const failed = error as { code?: number; stdout?: string; stderr?: string };
            throw new Error(
                `jason ${args.join(' ')} exited ${failed.code}: ${failed.stdout ?? ''} ${failed.stderr ?? ''}`,
                { cause: error },
            );
        }
    }
}

/** A free loopback port, for the one test that needs the same port before and after a restart. */
export function freePort(): Promise<number> {
    return new Promise((resolve, reject) => {
        const server = createServer();
        server.listen(0, '127.0.0.1', () => {
            const address = server.address();
            const port = typeof address === 'object' && address ? address.port : 0;
            server.close(() => (port ? resolve(port) : reject(new Error('no port'))));
        });
    });
}
