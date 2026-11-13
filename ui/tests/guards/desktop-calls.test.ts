import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { describe, expect, it } from 'vitest';
import { canIn, type DesktopMember } from '../../src/platform/places';
import { declaredMembers, problems } from './desktop-calls';

const sourceDir = join(import.meta.dirname, '..', '..', 'src');
const members = declaredMembers(readFileSync(join(sourceDir, 'platform', 'desktop.d.ts'), 'utf8'));
const tab = (member: string) => canIn('tab', member as DesktopMember);

function sources(dir: string): string[] {
    return readdirSync(dir).flatMap((entry) => {
        const path = join(dir, entry);
        if (statSync(path).isDirectory()) return entry === 'test' ? [] : sources(path);
        return /\.tsx?$/.test(entry) && !/\.test\.tsx?$/.test(entry) && !entry.endsWith('.d.ts') ? [path] : [];
    });
}

const of = (name: string, text: string) => problems(name, text, members, tab);

describe('the members of the desktop', () => {
    it('are read from the seam declaration', () => {
        expect(members.top).toEqual(new Set(['onCommand']));
        expect([...members.parts.keys()]).toEqual(
            expect.arrayContaining([
                'theme',
                'dailyTrends',
                'mode',
                'openIn',
                'pairing',
                'chat',
                'fullAccess',
                'links',
                'setup',
            ]),
        );
        expect(members.parts.get('chat')).toContain('send');
        expect(members.parts.get('dailyTrends')).toEqual(new Set(['get', 'set']));
    });
});

describe('every use of the desktop object in the page', () => {
    it('is a member the bridge carries, or stands behind the one question', () => {
        const found = sources(sourceDir).flatMap((path) =>
            of(relative(sourceDir, path).split(sep).join('/'), readFileSync(path, 'utf8')),
        );
        expect(found).toEqual([]);
    });
});

describe('the guard on the console source', () => {
    it('finds the use of a member whose question is taken out', () => {
        const chat = readFileSync(join(sourceDir, 'console', 'chat.tsx'), 'utf8');
        const without = chat.replace("if (!can('chat.remove')) return;", '');
        expect(without).not.toEqual(chat);
        expect(of('console/chat.tsx', without)).toEqual([expect.stringContaining('chat.remove is used outside')]);
    });
});

describe('the guard', () => {
    it('lets a bridge member through and refuses another member used bare', () => {
        expect(of('console/a.ts', `here().desktop.chat.send('a', 'b', {});`)).toEqual([]);
        expect(of('console/a.ts', `here().desktop.chat.remove('a');`)).toEqual([
            "console/a.ts:1: chat.remove is used outside the one question can('chat.remove')",
        ]);
    });

    it('takes a question in a function, a branch, a condition or a wrapper', () => {
        expect(of('a.ts', `function f(){ if (!can('chat.remove')) return; desktop.chat.remove('a'); }`)).toEqual([]);
        expect(of('a.ts', `if (can('chat.remove')) { desktop.chat.remove('a'); }`)).toEqual([]);
        expect(of('a.ts', `const x = can('links.take') ? desktop.links.take() : null;`)).toEqual([]);
        expect(of('a.ts', `can('links.take') && desktop.links.take();`)).toEqual([]);
        expect(of('a.ts', `defineReal('x', 'setup.replyPause', () => desktop.setup.replyPause('a'));`)).toEqual([]);
    });

    it('does not take a question about another member', () => {
        expect(of('a.ts', `function f(){ if (!can('chat.attach')) return; desktop.chat.remove('a'); }`)).toHaveLength(
            1,
        );
    });

    it('sees through a cast, an optional chain and the top-level members', () => {
        expect(of('a.ts', `(desktop as Partial<X>).chat?.remove?.('a');`)).toHaveLength(1);
        expect(of('a.ts', `desktop.onCommand(() => 1);`)).toHaveLength(1);
        expect(of('a.ts', `if (can('onCommand')) desktop.onCommand(() => 1);`)).toEqual([]);
    });

    it('refuses a part held under a name of its own, and a destructured object', () => {
        expect(of('a.ts', `const part = desktop.chat; part.remove('a');`)).toHaveLength(1);
        expect(of('a.ts', `const { chat } = here().desktop;`)).toHaveLength(1);
    });

    it('refuses a read of the window object outside its module', () => {
        expect(of('api/a.ts', `const d = window.jasonDesktop;`)).toHaveLength(1);
        expect(of('platform/window-desktop.ts', `window.jasonDesktop;`)).toEqual([]);
    });

    it('does not read a name that only looks like the object', () => {
        expect(of('a.ts', `chat.remove('a'); other.chat.remove('a'); const { chat } = other;`)).toEqual([]);
    });
});
