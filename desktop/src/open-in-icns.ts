/**
 * An application's own icon on macOS, read from its bundle's files and nothing else: no child process, no system tool. The
 * bundle's `Contents/Info.plist` is read only where it is XML text, for the name of its icon file (`CFBundleIconFile`);
 * that file, under `Contents/Resources/`, is an icon set (`.icns`), whose chunks of PNG data this module finds. Everything
 * it cannot read says null, and the menu keeps its own glyph: a binary property list, an application whose icon is only in
 * an asset catalog, an icon set too large or cut short, a chunk that is not PNG data.
 */

import { posix } from 'node:path';

/** The largest Info.plist and icon set read; a larger file is not an application's ordinary icon and is left alone. */
export const plistBytesAtMost = 1024 * 1024;
export const icnsBytesAtMost = 5 * 1024 * 1024;

const pngSignature = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

/** The chunk types of an icon set that hold PNG data (or JPEG 2000 in old sets, which is skipped by its signature). */
const pngTypes = new Set(['icp4', 'icp5', 'icp6', 'ic07', 'ic08', 'ic09', 'ic10', 'ic11', 'ic12', 'ic13', 'ic14']);

/** The edge, in pixels, past which a larger chunk is not preferred: decoding a 1024 px image to draw it at 40 is waste. */
const preferredAtMost = 512;

const entities: Readonly<Record<string, string>> = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" };

/**
 * The icon file an XML property list names, with `.icns` added where the name has no extension, as the system adds it;
 * null for a binary property list, a list with no such key, or a name that is not one plain file name.
 */
export function iconFileOf(plist: string): string | null {
    const text = plist.replace(/^﻿/, '');
    if (!/^\s*<(\?xml|!DOCTYPE plist|plist)/.test(text)) return null;
    const found = /<key>\s*CFBundleIconFile\s*<\/key>\s*<string>([^<]*)<\/string>/.exec(text);
    if (found === null) return null;
    const name = found[1]!.replace(/&(amp|lt|gt|quot|apos);/g, (_all, word: string) => entities[word]!).trim();
    if (name === '' || name === '.' || name === '..' || /[/\\\0]/.test(name)) return null;
    return posix.extname(name) === '' ? `${name}.icns` : name;
}

/** Where a bundle keeps its Info.plist, and the file its icon is in. */
export const plistOf = (app: string): string => posix.join(app, 'Contents', 'Info.plist');
export const resourceOf = (app: string, file: string): string => posix.join(app, 'Contents', 'Resources', file);

/**
 * The PNG data of the largest image an icon set holds at most 512 px wide, else of the largest; null where the file is
 * not an icon set or holds no PNG chunk whole. A chunk whose length runs past the end ends the walk, the chunks before it
 * still counted; a chunk's size is read from its PNG header, never trusted from its type.
 */
export function largestPng(bytes: Buffer): Buffer | null {
    if (bytes.length < 8 || bytes.toString('latin1', 0, 4) !== 'icns') return null;
    const end = Math.min(bytes.readUInt32BE(4), bytes.length);
    let best: { edge: number; data: Buffer } | null = null;
    const better = (edge: number): boolean => {
        if (best === null) return true;
        const small = edge <= preferredAtMost;
        const bestSmall = best.edge <= preferredAtMost;
        if (small !== bestSmall) return small;
        return edge > best.edge;
    };
    let at = 8;
    while (at + 8 <= end) {
        const type = bytes.toString('latin1', at, at + 4);
        const length = bytes.readUInt32BE(at + 4);
        if (length < 8 || at + length > end) break;
        const data = bytes.subarray(at + 8, at + length);
        // The PNG header: the signature, then the IHDR chunk with the width and the height.
        if (pngTypes.has(type) && data.length >= 24 && data.subarray(0, 8).equals(pngSignature) && data.toString('latin1', 12, 16) === 'IHDR') {
            const edge = Math.max(data.readUInt32BE(16), data.readUInt32BE(20));
            if (edge > 0 && better(edge)) best = { edge, data };
        }
        at += length;
    }
    return best === null ? null : Buffer.from(best.data);
}
