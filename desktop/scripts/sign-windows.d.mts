/** Whether a file of the package is signed here: every one but the bundled jason.exe. */
export function signs(file: string): boolean;
/** The signtool run that signs `file`, or null for a file left as it is. */
export function signRun(file: string, env?: Readonly<Record<string, string | undefined>>): { program: string; args: string[] } | null;
/** electron-builder's custom sign: signs the file the configuration names. */
export default function sign(configuration: { path: string }): Promise<void>;
