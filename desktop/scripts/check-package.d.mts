/** The fuses in the order of the wire, as @electron/fuses numbers them. */
export const wireOrder: readonly string[];
/** The binary Electron reads its fuses from: the program on Windows, the Electron framework inside the .app on macOS. */
export function fuseFile(app: string, platform: 'win32' | 'darwin'): string;
/** Where electron-builder writes app-update.yml: the resources folder beside the program, or inside the .app. */
export function updateFile(app: string, platform: 'win32' | 'darwin'): string;
/** What the package says, line by line; throws when it is not what it was configured to be. */
export function checkPackage(app: string, platform: 'win32' | 'darwin'): string[];
