import { describe, expect, it } from 'vitest';
import { hintFor, isShortcut, moved, plain, resultLimit, search, type Entry, type Kind } from './palette-model';

const words: Record<Kind, string> = {
    page: 'Page',
    settings: 'Settings',
    action: 'Action',
    agent: 'Agent',
    chat: 'Chat',
    person: 'Person',
};
const kindWord = (kind: Kind): string => words[kind];
const entry = (label: string, kind: Kind = 'page', extra = ''): Entry => ({
    key: `${kind}:${label}`,
    kind,
    label,
    words: extra,
});
const labels = (found: Entry[]) => found.map((each) => each.label);

describe('the palette finds entries', () => {
    const all = [
        entry('Needs you', 'page', 'approvals questions'),
        entry('Agents', 'page', 'campaigns'),
        entry('Accounts'),
        entry('Settings', 'settings'),
        entry('New GTM agent', 'action'),
        entry('Plan Q3 outreach', 'agent', 'Northwind rollout'),
    ];

    it('lists every entry when nothing is typed, and when only blanks are', () => {
        expect(search(all, '', kindWord)).toEqual(all);
        expect(search(all, '   ', kindWord)).toEqual(all);
    });

    it('finds by the label, by the kind word and by the extra words', () => {
        expect(labels(search(all, 'accounts', kindWord))).toEqual(['Accounts']);
        expect(labels(search(all, 'action', kindWord))).toEqual(['New GTM agent']);
        expect(labels(search(all, 'approvals', kindWord))).toEqual(['Needs you']);
        expect(labels(search(all, 'northwind', kindWord))).toEqual(['Plan Q3 outreach']);
    });

    it('needs every word typed, in any order, and ignores case and accents', () => {
        expect(labels(search(all, 'OUTREACH plan', kindWord))).toEqual(['Plan Q3 outreach']);
        expect(plain('Tomás ÁLVAREZ')).toBe('tomas alvarez');
        expect(labels(search([entry('Tomás Aguilar', 'agent')], 'tomas', kindWord))).toEqual(['Tomás Aguilar']);
    });

    it('puts a label that starts with the text first, then a word that does, then the rest', () => {
        // "Agents" starts with it; "New GTM agent" has a word that does; the last holds it only by its kind word.
        expect(labels(search(all, 'agent', kindWord))).toEqual(['Agents', 'New GTM agent', 'Plan Q3 outreach']);
    });

    it('finds nothing for a text nothing holds', () => {
        expect(search(all, 'zzzz', kindWord)).toEqual([]);
    });

    it('shows at most twelve', () => {
        const many = Array.from({ length: 30 }, (_, at) => entry(`Chat ${at}`, 'chat'));
        expect(search(many, '', kindWord)).toHaveLength(resultLimit);
        expect(search(many, 'chat', kindWord)).toHaveLength(12);
        expect(search(many, 'chat', kindWord, 3)).toHaveLength(3);
    });
});

describe('the shortcut and its name', () => {
    const press = (key: string, more: Partial<Parameters<typeof isShortcut>[0]> = {}) =>
        isShortcut({ key, ctrlKey: false, metaKey: false, altKey: false, shiftKey: false, ...more });

    it('is K with Ctrl or Meta alone', () => {
        expect(press('k', { ctrlKey: true })).toBe(true);
        expect(press('K', { metaKey: true })).toBe(true);
        expect(press('k')).toBe(false);
        expect(press('k', { ctrlKey: true, shiftKey: true })).toBe(false);
        expect(press('k', { metaKey: true, altKey: true })).toBe(false);
        expect(press('j', { ctrlKey: true })).toBe(false);
    });

    it('names the key by the system the window is on', () => {
        expect(hintFor('darwin')).toBe('mac');
        expect(hintFor('win32')).toBe('other');
        expect(hintFor('linux')).toBe('other');
    });

    it('moves the selection round both ends', () => {
        expect(moved(0, 1, 3)).toBe(1);
        expect(moved(2, 1, 3)).toBe(0);
        expect(moved(0, -1, 3)).toBe(2);
        expect(moved(0, 1, 0)).toBe(0);
    });
});
