import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { problems } from './console-templates';

const consoleDir = join(import.meta.dirname, '..', '..', 'src', 'console');
const sources = readdirSync(consoleDir).filter((name) => /\.tsx?$/.test(name) && !/\.test\.tsx?$/.test(name));

describe('the console source', () => {
    it('is read whole', () => {
        expect(sources).toEqual(
            expect.arrayContaining(['html.ts', 'chat.tsx', 'needs.ts', 'connect.ts', 'console.tsx']),
        );
    });

    it('makes markup only with the html tag, writes it only through paint, and carries no handler or style', () => {
        const found = sources.flatMap((name) => problems(name, readFileSync(join(consoleDir, name), 'utf8')));
        expect(found).toEqual([]);
    });
});

describe('the guard over it', () => {
    it('finds an inline handler, a style attribute and an interpolation nothing escapes', () => {
        expect(problems('a.ts', 'const a = html`<button onclick="go()">Go</button>`;')).toEqual([
            'a.ts:1 an inline handler in markup',
        ]);
        expect(problems('a.ts', 'const a = html`<div style="color:red">${name}</div>`;')).toEqual([
            'a.ts:1 a style attribute in markup',
        ]);
        expect(problems('a.ts', 'const a = `<b>${name}</b>`;')).toEqual([
            'a.ts:1 markup outside the html tag, where nothing escapes what it interpolates',
        ]);
        expect(problems('a.ts', "const a = '<b>' + name + '</b>';")).toHaveLength(2);
    });

    it('finds markup written or made past the tag, and a style in JSX', () => {
        expect(problems('a.ts', 'element.innerHTML = text;')).toEqual([
            'a.ts:1 markup written into the page outside html.ts',
        ]);
        expect(problems('a.ts', "element['outerHTML'] = text;")).toHaveLength(1);
        expect(problems('a.ts', 'element.insertAdjacentHTML("beforeend", text);')).toHaveLength(1);
        expect(problems('a.ts', 'const a = Html.made(text);')).toEqual(['a.ts:1 markup made outside the html tag']);
        expect(problems('a.tsx', 'const a = <div style={{ color: "red" }} />;')).toEqual([
            'a.tsx:1 a style attribute in JSX',
        ]);
        expect(problems('a.tsx', 'const a = <div dangerouslySetInnerHTML={{ __html: text }} />;')).toHaveLength(1);
    });

    it('lets the tag and the one painter be', () => {
        expect(problems('a.ts', 'const a = html`<b>${name}</b><i data-action="x">${html`<u>${n}</u>`}</i>`;')).toEqual(
            [],
        );
        expect(problems('html.ts', 'export function paint(into, value) { into.innerHTML = value.markup; }')).toEqual(
            [],
        );
        expect(problems('a.ts', "const words = 'a < b and c > d';")).toEqual([]);
    });
});
