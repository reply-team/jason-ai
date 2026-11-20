// The shape of electron-builder.config.mjs as its tests read it: only the parts this configuration sets.

export const signer: string;
export const fuses: Readonly<Record<string, boolean>>;
export const chatFiles: readonly string[];
export function electronChecksums(): Record<string, string>;
export function bundleFiles(platform: string): string[];
export function releaseVersion(value: string | undefined): string;
export function bundleFolder(arch: number): string;
export function checkBundle(folder: string, version: string, platform: string): void;
export function copyChromiumNotices(from: string, resources: string): void;
/** The variable that asks for a test build: every signature step and check off, for CI alone. */
export const testBuildVariable: string;

interface Target {
    target: string;
    arch?: string[];
}

export interface Configuration {
    appId: string;
    productName: string;
    copyright: string;
    extraMetadata: { version: string; homepage: string };
    directories: { output: string; buildResources: string };
    files: string[];
    asar: boolean;
    npmRebuild: boolean;
    extraResources: { from: string; to: string }[];
    electronDownload: { checksums: Record<string, string> };
    electronFuses: Record<string, boolean>;
    publish: { provider: string; owner: string; repo: string; releaseType: string }[];
    detectUpdateChannel: boolean;
    generateUpdatesFilesForAllChannels: boolean;
    beforePack: (context: unknown) => Promise<void>;
    afterPack: (context: unknown) => Promise<void>;
    artifactBuildStarted: (event: unknown) => Promise<void>;
    artifactBuildCompleted: (event: unknown) => void;
    win: {
        target: Target[];
        icon: string;
        requestedExecutionLevel: string;
        verifyUpdateCodeSignature: boolean;
        signExts: string[];
        signtoolOptions: { publisherName: string[]; signingHashAlgorithms: string[]; sign?: string };
        forceCodeSigning?: boolean;
    };
    nsis: { oneClick: boolean; perMachine: boolean; artifactName: string; include: string; deleteAppDataOnUninstall: boolean };
    mac: {
        target: Target[];
        category: string;
        icon: string;
        /** "-" in a test build alone: an ad hoc signature. */
        identity?: string;
        hardenedRuntime: boolean;
        entitlements: string;
        entitlementsInherit: string;
        signIgnore: string[];
        notarize: boolean;
        forceCodeSigning: boolean;
        artifactName: string;
        /** The desktop's jason-desktop link scheme, written into Info.plist as CFBundleURLTypes. */
        protocols: { name: string; schemes: string[] }[];
    };
    dmg: { sign: boolean; writeUpdateInfo: boolean; artifactName: string };
    linux: {
        target: Target[];
        executableName: string;
        executableArgs: string[];
        icon: string;
        category: string;
        synopsis: string;
        maintainer: string;
    };
    appImage: { artifactName: string };
    /** The deb's menu entry declares the jason link scheme by its MIME type; the AppImage's declares none. */
    deb: { artifactName: string; recommends: string[]; mimeTypes: string[]; executableArgs: string[] };
    toolsets: { appimage: string };
}

export function configuration(options: { version: string; sign?: string | null; testBuild?: boolean; linuxWaitMs?: number }): Configuration;
export function fromEnvironment(environment: Record<string, string | undefined>): Configuration;
export default function (): Configuration;
