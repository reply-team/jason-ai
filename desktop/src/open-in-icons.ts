/**
 * "Open in", each application's own icon: read here from the application this desktop found, and handed to the page as
 * pixels, never as a path or a file. The page paints them on a canvas, so its content policy is unchanged.
 *
 * Windows: the icon the program's own file carries, by the system (`app.getFileIcon`). The system answers an unreadable
 * file with the generic icon of a program, which is not told apart and is drawn as it comes. macOS: the bundle's own icon
 * set, read by `open-in-icns.ts`. Linux: none; the menu keeps its glyphs.
 *
 * Each path is read once a run, again only when its modified time changes; one answer is kept per path, the last, and an
 * answer of no icon is kept as one, while a read that failed is not kept and is tried again the next time. Nothing is
 * written to disk. A read slower than `waitMs` is not waited for: the menu is drawn with the glyph, and the icon the read
 * finds is there the next time the menu is read. A file of a bundle is read only where it is a regular file, and never
 * past its cap, however large it is or grows.
 */

import { constants } from 'node:fs';
import { open } from 'node:fs/promises';
import { icnsBytesAtMost, iconFileOf, largestPng, plistBytesAtMost, plistOf, resourceOf } from './open-in-icns.ts';

/** An icon as the page receives it: its size in pixels and its pixels, RGBA, not premultiplied, base64. */
export interface PlaceIcon {
    width: number;
    height: number;
    rgba: string;
}

/** What this module needs of Electron's `NativeImage`. */
export interface IconImage {
    isEmpty(): boolean;
    /** The size of the representation nearest the scale factor, the one `toBitmap` of the same factor gives. */
    getSize(scaleFactor?: number): { width: number; height: number };
    resize(options: { width: number; height: number; quality?: 'good' | 'better' | 'best' }): IconImage;
    /** BGRA, premultiplied, as Skia keeps an image on every system Jason runs on. */
    toBitmap(options?: { scaleFactor?: number }): Buffer;
}

export interface IconReaderDeps {
    platform: NodeJS.Platform;
    stat(path: string): Promise<{ mtimeMs: number; size: number; isFile(): boolean }>;
    /** A regular file's bytes, or null for anything else or for more than `cap` bytes (`readRegularFileAtMost`). */
    readAtMost(path: string, cap: number): Promise<Buffer | null>;
    /** Windows: the icon a program's file carries, 32 px (`app.getFileIcon(path, { size: 'normal' })`). */
    fileIcon(path: string): Promise<IconImage>;
    /** An image decoded from PNG data (`nativeImage.createFromBuffer`). */
    fromBuffer(png: Buffer): IconImage;
    /** How long the menu waits for one read; 500 ms when not said. */
    waitMs?: number;
}

/** The edge the menu draws an icon at, in CSS pixels, and the edge of the pixels it is given: twice it, for a 200% screen. */
export const drawnEdge = 20;
export const pixelEdge = drawnEdge * 2;

/** BGRA premultiplied, as `toBitmap` gives it, to RGBA straight, as a canvas's `ImageData` takes it. */
export function rgbaOf(bgra: Buffer): Buffer {
    const out = Buffer.alloc(bgra.length - (bgra.length % 4));
    for (let at = 0; at < out.length; at += 4) {
        const alpha = bgra[at + 3]!;
        const straight = (value: number) => (alpha === 0 ? 0 : alpha === 255 ? value : Math.min(255, Math.round((value * 255) / alpha)));
        out[at] = straight(bgra[at + 2]!);
        out[at + 1] = straight(bgra[at + 1]!);
        out[at + 2] = straight(bgra[at]!);
        out[at + 3] = alpha;
    }
    return out;
}

/**
 * A regular file's bytes, read through one handle and never more than `cap` of them; null where the path is no regular file
 * (a folder, a device, a pipe, by a link or not) or holds more than `cap` bytes, the bytes it gained after it was opened
 * included. Opened without waiting where the system allows it, so a pipe put in the file's place does not hold the read.
 */
export async function readRegularFileAtMost(path: string, cap: number): Promise<Buffer | null> {
    let handle;
    try {
        handle = await open(path, constants.O_RDONLY | (constants.O_NONBLOCK ?? 0));
    } catch {
        return null;
    }
    try {
        const stats = await handle.stat();
        if (!stats.isFile() || stats.size > cap) return null;
        // One byte more than the size or the cap: a read that fills it says the file is larger than either.
        const buffer = Buffer.alloc(Math.min(stats.size, cap) + 1);
        let length = 0;
        while (length < buffer.length) {
            const { bytesRead } = await handle.read(buffer, length, buffer.length - length, length);
            if (bytesRead === 0) break;
            length += bytesRead;
        }
        return length === buffer.length ? null : buffer.subarray(0, length);
    } finally {
        await handle.close();
    }
}

/** An image as the page receives it, or null for an empty one or one whose pixels do not fill its size. */
export function iconOf(image: IconImage): PlaceIcon | null {
    if (image.isEmpty()) return null;
    // Both read at the same scale, so the pixels and the size describe one representation of the image.
    const { width, height } = image.getSize(1);
    const bitmap = image.toBitmap({ scaleFactor: 1 });
    if (width <= 0 || height <= 0 || bitmap.length !== width * height * 4) return null;
    return { width, height, rgba: rgbaOf(bitmap).toString('base64') };
}

export class IconReader {
    readonly #deps: IconReaderDeps;
    /** By path, the last answer read and the modified time it was read at: one entry a path, so it never grows past them. */
    readonly #kept = new Map<string, { changed: number; icon: Promise<PlaceIcon | null> }>();

    constructor(deps: IconReaderDeps) {
        this.#deps = deps;
    }

    /** The icon of the application at a path the desktop found, or null; never waits longer than the menu does. */
    async icon(path: string): Promise<PlaceIcon | null> {
        const deps = this.#deps;
        if (deps.platform !== 'win32' && deps.platform !== 'darwin') return null;
        let timer: NodeJS.Timeout | undefined;
        const late = new Promise<null>((resolve) => {
            timer = setTimeout(() => resolve(null), deps.waitMs ?? 500);
        });
        try {
            return await Promise.race([this.#read(path), late]);
        } finally {
            clearTimeout(timer);
        }
    }

    async #read(path: string): Promise<PlaceIcon | null> {
        let changed: number;
        try {
            changed = (await this.#deps.stat(path)).mtimeMs;
        } catch {
            return null;
        }
        const kept = this.#kept.get(path);
        if (kept !== undefined && kept.changed === changed) return kept.icon;
        const entry = { changed, icon: Promise.resolve<PlaceIcon | null>(null) };
        // A read that failed is not an answer: it is dropped, so the next time the menu is read asks again.
        entry.icon = (this.#deps.platform === 'win32' ? this.#windows(path) : this.#mac(path)).catch(() => {
            if (this.#kept.get(path) === entry) this.#kept.delete(path);
            return null;
        });
        this.#kept.set(path, entry);
        return entry.icon;
    }

    async #windows(path: string): Promise<PlaceIcon | null> {
        return iconOf(await this.#deps.fileIcon(path));
    }

    async #mac(app: string): Promise<PlaceIcon | null> {
        const deps = this.#deps;
        const plist = await this.#regular(plistOf(app), plistBytesAtMost);
        if (plist === null) return null;
        const file = iconFileOf(plist.toString('utf8'));
        if (file === null) return null;
        const icns = await this.#regular(resourceOf(app, file), icnsBytesAtMost);
        if (icns === null) return null;
        const png = largestPng(icns);
        if (png === null) return null;
        const image = deps.fromBuffer(png);
        if (image.isEmpty()) return null;
        return iconOf(image.resize({ width: pixelEdge, height: pixelEdge, quality: 'best' }));
    }

    /** A bundle's file, where it is a regular file within its cap, else null: a file that is not there is no icon, too. */
    async #regular(path: string, cap: number): Promise<Buffer | null> {
        let stats;
        try {
            stats = await this.#deps.stat(path);
        } catch {
            return null;
        }
        if (!stats.isFile() || stats.size > cap) return null;
        return this.#deps.readAtMost(path, cap);
    }
}
