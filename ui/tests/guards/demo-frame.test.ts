import { readdirSync, readFileSync } from 'node:fs';
import { join, sep } from 'node:path';
import { describe, expect, it } from 'vitest';
import { demoFrame, demoStrip } from '../../src/platform/demo-page';

// The demo page is content the console does not trust, shown in a frame of the console's own. Three things hold it: the
// switch stands where the desktop's does, over the page's strip, so one page serves the window and the tab; the frame
// is the one frame the console draws, with its attributes in one place; and the console listens to no message, so
// nothing the page posts to the window above it is ever read.

const repo = join(import.meta.dirname, '..', '..', '..');
const src = join(repo, 'ui', 'src');

/** Every source of the console's page, tests aside, by its path under src. */
const sources = readdirSync(src, { recursive: true, encoding: 'utf8' })
    .map((name) => name.split(sep).join('/'))
    .filter((name) => /\.tsx?$/.test(name) && !/\.test\.tsx?$/.test(name) && !name.startsWith('test/'));

/** The sources that listen for a message: a `message` event, or an `onmessage` handler. */
export function listeners(text: string): string[] {
    return [...text.matchAll(/addEventListener\(\s*['"`]message['"`]|\bonmessage\s*=/g)].map((match) => match[0]);
}

/** The frames a source draws: in JSX, or made by the DOM. */
export function frames(text: string): string[] {
    return [...text.matchAll(/<iframe\b|createElement\(\s*['"`]iframe['"`]/gi)].map((match) => match[0]);
}

describe('the demo’s switch over the page', () => {
    it('stands over the strip the desktop stands its own over, as high as it', () => {
        const frame = readFileSync(join(repo, 'desktop', 'src', 'window-frame.ts'), 'utf8');
        const page = readFileSync(join(repo, 'desktop', 'src', 'demo-page.ts'), 'utf8');
        const stripHeight = /export const stripHeight = (\d+);/.exec(frame)?.[1];
        expect(stripHeight).toBeDefined();
        // The desktop's own numbers for the page take the strip's height from that one constant.
        expect(page).toMatch(/export const demoGeometry = \{[^}]*\bstrip: stripHeight\b[^}]*\}/);
        expect(demoStrip.height).toBe(Number(stripHeight));
    });
});

describe('the frame', () => {
    it('is drawn by the demo’s view alone, from its one constant', () => {
        const drawing = sources.filter((name) => frames(readFileSync(join(src, name), 'utf8')).length > 0);
        expect(drawing).toEqual(['platform/demo-frame.tsx']);
        const view = readFileSync(join(src, 'platform', 'demo-frame.tsx'), 'utf8');
        const element = /<iframe\b(?<attributes>[^>]*)\/>/.exec(view)?.groups?.attributes ?? '';
        const named = [...element.matchAll(/\b([a-zA-Z]+)=(\{[^}]*\}|"[^"]*")/g)].map(([, name, value]) => [
            name,
            value,
        ]);
        expect(named).toEqual([
            ['className', '"demo-frame"'],
            ...Object.keys(demoFrame).map((key) => [key, `{demoFrame.${key}}`]),
        ]);
    });

    it('keeps the page in an origin of its own, with scripts alone and nothing delegated', () => {
        expect(demoFrame.sandbox.split(' ')).toEqual(['allow-scripts']);
        expect(demoFrame.allow).toBe('');
        expect(demoFrame.referrerPolicy).toBe('no-referrer');
        expect(demoFrame.src).toBe('sealed/demo.html');
    });
});

describe('a message from the page', () => {
    it('is listened for nowhere but on the journal’s own socket', () => {
        const listening = sources.filter((name) => listeners(readFileSync(join(src, name), 'utf8')).length > 0);
        expect(listening).toEqual(['platform/journal-stream.ts']);
        const stream = readFileSync(join(src, 'platform', 'journal-stream.ts'), 'utf8');
        expect(stream).not.toMatch(
            /\b(window|self|globalThis|top|parent)\s*\.\s*(addEventListener\(\s*['"`]message|onmessage)/,
        );
        expect(stream).not.toMatch(/^\s*(addEventListener\(\s*['"`]message|onmessage\s*=)/m);
    });
});

describe('the reading of a source', () => {
    it('finds a message listener in either form', () => {
        expect(listeners("window.addEventListener('message', on);")).toHaveLength(1);
        expect(listeners('addEventListener( "message" , on)')).toHaveLength(1);
        expect(listeners('window.onmessage = on;')).toHaveLength(1);
        expect(listeners("addEventListener('messages', on); const onmessages = 1;")).toEqual([]);
    });

    it('finds a frame in JSX and one the DOM makes', () => {
        expect(frames('<iframe src="x" />')).toHaveLength(1);
        expect(frames("document.createElement('iframe')")).toHaveLength(1);
        expect(frames("const dropped = new Set(['iframe', 'frame']);")).toEqual([]);
    });
});
