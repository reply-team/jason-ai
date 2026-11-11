/** Whether a file of the package is signed here: every one but the bundled jason.exe. */
export function signs(file: string): boolean;
/** Whether the program image `file` already carries an Authenticode signature of its own. */
export function carriesSignature(file: string): boolean;
/** The signtool run that signs `file`, or null for a file left as it is: the bundled jason.exe, or one that came signed. */
export function signRun(file: string, env?: Readonly<Record<string, string | undefined>>): { program: string; args: string[] } | null;
/** electron-builder's custom sign: signs the file the configuration names. */
export default function sign(configuration: { path: string }): Promise<void>;
