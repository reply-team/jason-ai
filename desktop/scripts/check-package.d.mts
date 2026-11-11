/** The platforms a package is built for, and a Linux package's two forms. */
export type Platform = 'win32' | 'darwin' | 'linux';
export type Form = 'deb' | 'appimage';
/** The fuses in the order of the wire, as @electron/fuses numbers them. */
export const wireOrder: readonly string[];
/** The binary Electron reads its fuses from: the program on Windows and Linux, the Electron framework inside the .app on macOS. */
export function fuseFile(app: string, platform: Platform): string;
/** Where electron-builder writes app-update.yml: the resources folder beside the program, or inside the .app. */
export function updateFile(app: string, platform: Platform): string;
/** What the package says, line by line; throws when it is not what it was configured to be. */
export function checkPackage(app: string, platform: Platform, form?: Form): string[];
