import { describe, expect, it } from 'vitest';
import { conversationItems, rowItems, type ConversationFacts, type Gates, type MenuItem } from './conversation-items';

const kept: ConversationFacts = {
    kept: 'conv_1',
    id: 'conv_1',
    campaignId: null,
    running: false,
    removing: false,
    host: null,
};
const everything: Gates = {
    create: true,
    rename: true,
    restart: true,
    remove: true,
    tab: false,
    folder: 'Open in File Explorer',
    hosts: [],
};

/** Each entry by its word, a held one marked, a separator as a dash. */
const said = (items: MenuItem[]) =>
    items.map((each) =>
        each.kind === 'separator' ? '-' : 'disabled' in each && each.disabled ? `${each.label} (held)` : each.label,
    );

describe('Open in the file manager', () => {
    const agent: ConversationFacts = { ...kept, campaignId: 'cmp_1' };
    const rest = ['-', 'Rename conversation', 'Start conversation fresh', '-', 'Remove conversation'];

    it('stands right after Open on the row of a chat about an agent, read or not', () => {
        expect(said(rowItems({ status: 'active' }, agent, everything))).toEqual([
            'Open',
            'Open in File Explorer',
            'Pause',
            'Archive…',
            ...rest,
        ]);
        expect(said(rowItems(null, agent, everything))).toEqual(['Open', 'Open in File Explorer', ...rest]);
    });

    it('stands first in the agent’s “…”, before the conversation’s acts', () => {
        expect(said(conversationItems(agent, everything))).toEqual([
            'Open in File Explorer',
            'Rename conversation',
            'Start conversation fresh',
            'Remove conversation',
        ]);
    });

    it('is offered for no chat about no agent, in no tab, and nowhere the place names no file manager', () => {
        expect(said(rowItems(null, kept, everything))).not.toContain('Open in File Explorer');
        expect(said(rowItems({ status: 'active' }, agent, { ...everything, tab: true }))).toEqual([
            'Open',
            'Pause',
            'Archive…',
            '-',
            'Rename conversation',
        ]);
        expect(said(conversationItems(agent, { ...everything, folder: null }))).toEqual([
            'Rename conversation',
            'Start conversation fresh',
            'Remove conversation',
        ]);
    });
});

describe('Open the host in terminal', () => {
    const agent: ConversationFacts = { ...kept, campaignId: 'cmp_1' };
    const both: Gates = {
        ...everything,
        hosts: [
            { id: 'claude', label: 'Claude Code' },
            { id: 'codex', label: 'Codex' },
        ],
    };
    const sessionOf = (items: MenuItem[]) => items.find((each) => each.kind === 'session');

    it('stands right after the folder in both menus, one item when both hosts are found', () => {
        expect(said(conversationItems(agent, both))).toEqual([
            'Open in File Explorer',
            'Open Claude Code in terminal',
            'Rename conversation',
            'Start conversation fresh',
            'Remove conversation',
        ]);
        expect(said(rowItems({ status: 'active' }, agent, both)).slice(0, 4)).toEqual([
            'Open',
            'Open in File Explorer',
            'Open Claude Code in terminal',
            'Pause',
        ]);
        expect(sessionOf(conversationItems(agent, both))).toMatchObject({
            host: 'claude',
            title: "Opens a new Claude Code session in a terminal at this agent's folder",
        });
    });

    it('names the host the agent’s chat is set to use where it is found, else the first found', () => {
        expect(sessionOf(conversationItems({ ...agent, host: 'codex' }, both))).toMatchObject({
            host: 'codex',
            label: 'Open Codex in terminal',
            title: "Opens a new Codex session in a terminal at this agent's folder",
        });
        // A host the chat is set to that this computer lacks, or one that is no terminal's, gives way to the first found.
        const codexOnly: Gates = { ...everything, hosts: [{ id: 'codex', label: 'Codex' }] };
        expect(sessionOf(conversationItems({ ...agent, host: 'claude' }, codexOnly))).toMatchObject({
            host: 'codex',
            label: 'Open Codex in terminal',
        });
        expect(sessionOf(conversationItems({ ...agent, host: 'deepseek' }, both))).toMatchObject({ host: 'claude' });
    });

    it('is offered for no chat about no agent, in no tab, and where no host is found', () => {
        expect(said(conversationItems(kept, both))).not.toContain('Open Claude Code in terminal');
        expect(said(conversationItems(agent, { ...both, tab: true }))).not.toContain('Open Claude Code in terminal');
        expect(sessionOf(conversationItems(agent, everything))).toBeUndefined();
        // It stands on its own where the place names no file manager.
        expect(said(conversationItems(agent, { ...both, folder: null }))[0]).toBe('Open Claude Code in terminal');
    });
});

describe('the menu on a row of Recent', () => {
    it('offers an agent the move its status allows, Archive…, and the conversation’s acts, Remove last', () => {
        const rest = ['-', 'Rename conversation', 'Start conversation fresh', '-', 'Remove conversation'];
        expect(said(rowItems({ status: 'active' }, kept, everything))).toEqual(['Open', 'Pause', 'Archive…', ...rest]);
        expect(said(rowItems({ status: 'paused' }, kept, everything))).toEqual(['Open', 'Resume', 'Archive…', ...rest]);
        expect(said(rowItems({ status: 'draft' }, kept, everything))).toEqual(['Open', 'Start', 'Archive…', ...rest]);
    });

    it('offers an archived agent neither a move nor Archive…', () => {
        expect(said(rowItems({ status: 'archived' }, kept, everything))).toEqual([
            'Open',
            '-',
            'Rename conversation',
            'Start conversation fresh',
            '-',
            'Remove conversation',
        ]);
    });

    it('offers a chat about no agent, and an agent not read yet, Open and the conversation’s acts alone', () => {
        const alone = ['Open', '-', 'Rename conversation', 'Start conversation fresh', '-', 'Remove conversation'];
        expect(said(rowItems(null, kept, everything))).toEqual(alone);
    });

    it('in a tab the desktop opened leaves out Start fresh and Remove, and a separator with nothing after it', () => {
        expect(said(rowItems({ status: 'active' }, kept, { ...everything, tab: true }))).toEqual([
            'Open',
            'Pause',
            'Archive…',
            '-',
            'Rename conversation',
        ]);
    });

    it('holds Start fresh and Remove while the conversation’s turn runs or its removal is asked, and Rename while it is removed', () => {
        expect(said(rowItems(null, { ...kept, running: true }, everything))).toEqual([
            'Open',
            '-',
            'Rename conversation',
            'Start conversation fresh (held)',
            '-',
            'Remove conversation (held)',
        ]);
        expect(said(rowItems(null, { ...kept, removing: true }, everything))).toEqual([
            'Open',
            '-',
            'Rename conversation (held)',
            'Start conversation fresh (held)',
            '-',
            'Remove conversation (held)',
        ]);
    });

    it('leaves out each act its place does not allow, and every one where the place makes no chat', () => {
        expect(said(rowItems(null, kept, { ...everything, rename: false }))).toEqual([
            'Open',
            '-',
            'Start conversation fresh',
            '-',
            'Remove conversation',
        ]);
        expect(said(rowItems(null, kept, { ...everything, restart: false }))).toEqual([
            'Open',
            '-',
            'Rename conversation',
            '-',
            'Remove conversation',
        ]);
        expect(said(rowItems(null, kept, { ...everything, remove: false }))).toEqual([
            'Open',
            '-',
            'Rename conversation',
            'Start conversation fresh',
        ]);
        expect(said(rowItems({ status: 'paused' }, kept, { ...everything, create: false }))).toEqual([
            'Open',
            'Resume',
            'Archive…',
        ]);
    });
});

describe('the conversation’s acts', () => {
    it('are the agent’s “…” as it always held them, Rename waiting for a kept conversation', () => {
        expect(said(conversationItems(kept, everything))).toEqual([
            'Rename conversation',
            'Start conversation fresh',
            'Remove conversation',
        ]);
        const unkept = conversationItems(
            { kept: undefined, id: 'unmade:1', campaignId: null, running: false, removing: false, host: null },
            everything,
        );
        expect(said(unkept)).toEqual([
            'Rename conversation (held)',
            'Start conversation fresh (held)',
            'Remove conversation (held)',
        ]);
        expect(unkept[0]).toMatchObject({ title: 'Send a message first; then the chat can be renamed' });
        expect(conversationItems(kept, { ...everything, create: false })).toEqual([]);
    });
});
