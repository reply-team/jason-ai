// The shape of electron-builder.config.mjs as its tests read it: only the parts this configuration sets.

export const signer: string;
export const fuses: Readonly<Record<string, boolean>>;
export const chatLists: readonly string[];
export function bundleFiles(platform: string): string[];
export function releaseVersion(value: string | undefined): string;
export function bundleFolder(arch: number): string;
export function checkBundle(folder: string, version: string, platform: string): void;
export function copyChromiumNotices(appOutDir: string, resources: string): void;

interface Target {
    target: string;
    arch: string[];
}

export interface Configuration {
    appId: string;
    productName: string;
    copyright: string;
    extraMetadata: { version: string };
    directories: { output: string; buildResources: string };
    files: string[];
    asar: boolean;
    extraResources: { from: string; to: string }[];
    electronFuses: Record<string, boolean>;
    publish: { provider: string; owner: string; repo: string; releaseType: string }[];
    beforePack: (context: unknown) => Promise<void>;
    afterPack: (context: unknown) => Promise<void>;
    win: {
        target: Target[];
        icon: string;
        requestedExecutionLevel: string;
        verifyUpdateCodeSignature: boolean;
        signtoolOptions: { publisherName: string[]; signingHashAlgorithms: string[]; sign?: string };
        forceCodeSigning?: boolean;
    };
    nsis: { oneClick: boolean; perMachine: boolean; artifactName: string; include: string; deleteAppDataOnUninstall: boolean };
    mac: {
        target: Target[];
        category: string;
        icon: string;
        hardenedRuntime: boolean;
        entitlements: string;
        entitlementsInherit: string;
        signIgnore: string[];
        notarize: boolean;
        forceCodeSigning: boolean;
        artifactName: string;
    };
    dmg: { sign: boolean; artifactName: string };
}

export function configuration(options: { version: string; sign?: string | null }): Configuration;
export default function (): Configuration;
