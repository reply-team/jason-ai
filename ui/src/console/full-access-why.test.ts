// Why a card of a Full access turn asks, in the page's words: one row per code the desktop names, pinned, and the card
// that draws it.
import { describe, expect, it } from 'vitest';
import type { DesktopFullAccessWhy } from '../platform/desktop';
import { fromRecord, fullAccessWhyWords, newChat, received, shown } from './chat-model';

/** Every code and its sentence, a tool's name standing where a code names one. */
const rows: [DesktopFullAccessWhy['reason'], string][] = [
    [
        'command.expansion',
        'This command has something the shell expands only as it runs, such as a variable or a substitution, so Jason cannot read plainly what it would do.',
    ],
    [
        'command.escape',
        'This command has a backslash escape, which changes how the shell reads the characters after it, so Jason cannot read plainly what it would do.',
    ],
    [
        'command.glob',
        'This command has a wildcard (*, ? or [ ]), which the shell turns into file names only as it runs, so Jason cannot read plainly what it would touch.',
    ],
    [
        'command.brace',
        'This command has braces, which the shell can turn into several words as it runs, so Jason cannot read plainly what it would do.',
    ],
    [
        'command.group',
        'This command has parentheses, which the shell can read as a group of commands or a command of its own, so Jason cannot read plainly what it would do.',
    ],
    [
        'command.history',
        'This command has an exclamation mark, which the shell can read as a call back to an earlier command, so Jason cannot read plainly what it would do.',
    ],
    [
        'command.comment',
        'This command has a #, which the shell can read as the start of a comment that hides the rest, so Jason cannot read plainly what it would do.',
    ],
    [
        'command.tilde',
        'This command has a ~, which Jason does not read plainly in this shell, so it cannot tell what the command would touch.',
    ],
    [
        'command.non_ascii',
        'This command has a character outside plain ASCII, which can look like another character on the card, so Jason does not run it without you.',
    ],
    [
        'command.control_character',
        'This command has a control character, which may not show on the card, so Jason does not run it without you.',
    ],
    [
        'command.character',
        'This command has a character Jason does not know how this shell would read, so it cannot read plainly what the command would do.',
    ],
    [
        'command.operator',
        'This command has an operator, a redirection, a line break or a device path (/dev/...) Jason does not read, so it cannot tell plainly what the command would do.',
    ],
    [
        'command.background',
        'This command runs something in the background (&), which goes on after the command returns, so Jason does not run it without you.',
    ],
    [
        'command.assignment',
        'This command sets a variable for what it runs, which can change what that program does, so Jason does not run it without you.',
    ],
    [
        'command.here_document',
        'This command has a here-document, a block of text handed to a program, which Jason does not read as part of the command.',
    ],
    [
        'command.process_substitution',
        'This command has a process substitution, which runs another command in the middle of this one, so Jason cannot read plainly what it would do.',
    ],
    [
        'command.quote',
        'This command has quoting Jason cannot read plainly, so it cannot tell where each word begins and ends.',
    ],
    [
        'command.array',
        'This command has an @ or a comma, which PowerShell can read as an array of several words, so Jason cannot read plainly what it would do.',
    ],
    [
        'command.stop_parsing',
        "This command has PowerShell's stop-parsing token (--%), after which PowerShell passes the rest on unread, so Jason cannot read plainly what it would do.",
    ],
    [
        'command.call_operator',
        "This command has PowerShell's call operator (& or .), which runs what follows it as a command, so Jason cannot read plainly what it would do.",
    ],
    ['command.syntax', 'Jason could not read this command, so it does not run it without you.'],
    ['command.unknown_shell', 'Full access does not run commands in this shell without asking you.'],
    [
        'command.command_prompt',
        'This command could run through cmd, as a batch file or by a % in what it passes on, which Jason does not read, so it cannot tell what it would do.',
    ],
    [
        'command.inline_code',
        'This command hands a program code as text to run, which Jason does not read, so it cannot tell what it would do.',
    ],
    [
        'command.runner',
        'This command starts another program to run something, and Jason does not read what that program would run.',
    ],
    [
        'command.builtin',
        "This command uses a shell built-in that changes the shell itself (aliases, variables, options, traps, modules or the programs its names run), or reaches a function, an alias or a variable by its provider's name, so Jason cannot read plainly what the rest would do.",
    ],
    ['command.servers', 'This command would change the MCP servers or plugins an AI loads, or sign a server in.'],
    [
        'command.directory_change',
        'This command moves to a folder Jason cannot place, so it cannot tell what the commands after it would touch.',
    ],
    [
        'command.reply',
        "This command runs reply in a form Jason does not run on its own, and reply can act on the provider directly, outside Jason's approvals.",
    ],
    [
        'command.guarded_name',
        "This command names a file or folder Jason guards: an AI's settings, skills or instructions, the MCP servers it loads, a repository's own files, or a folder's Jason marker.",
    ],
    [
        'command.search',
        'This command names a variable that decides where the shell looks for programs, which decides what later commands run.',
    ],
    [
        'command.local_jason',
        "This command names a file called jason in a working folder, which the shell could run in place of the desktop's own.",
    ],
    [
        'command.holds_data',
        "This command names a folder that holds Jason's own data, or in a demo one of your own folders, so it could reach into it.",
    ],
    ['command.link', 'This command makes or follows a link, which can lead somewhere its path does not show.'],
    [
        'request.own_tool',
        'This is a tool of one of your own MCP servers, and Jason cannot read what it does with what it is given.',
    ],
    ['request.settings', "This would change the AI's own settings, which act on what it runs next."],
    ['request.repository', "This would change a repository's own files, such as the hooks git runs."],
    ['request.marker', "This would change a folder's Jason marker, which is how Jason knows the folder."],
    ['request.loads', 'This would change the skills or settings an AI loads.'],
    ['request.instructions', 'This would change instructions an AI reads.'],
    ['request.server_list', 'This would change the MCP servers an AI loads.'],
    ['request.local_jason', "This would write or run a jason that is not the desktop's own."],
    ['request.search', 'This would change where the shell finds programs, which decides what later commands run.'],
    [
        'request.windows_name',
        'This uses a path Windows reads as another name, so Jason cannot be sure which file it reaches.',
    ],
    ['request.tilde_path', 'This uses a path that begins with ~, which Jason does not read as a place.'],
    [
        'request.no_file_named',
        'Frobnicate was asked to act with no file named, so Jason cannot tell which file it would touch.',
    ],
    ['request.unknown_tool', 'Jason does not know the tool Frobnicate, so it cannot tell what it would do.'],
    ['request.other', 'Full access does not run this kind of request on its own, so it asks you.'],
    [
        'host_asked',
        'Claude Code asked about this one itself, though Full access would run it, so it comes to you as it would in Ask first.',
    ],
];

const why = (reason: DesktopFullAccessWhy['reason']): DesktopFullAccessWhy =>
    reason === 'request.no_file_named' || reason === 'request.unknown_tool'
        ? { reason, tool: 'Frobnicate' }
        : ({ reason } as DesktopFullAccessWhy);

describe('why Full access asks', () => {
    it.each(rows)('says %s in one sentence', (reason, sentence) => {
        expect(fullAccessWhyWords(why(reason))).toBe(sentence);
    });

    it('has a row for every code, each said once, and no sentence left with a slot unfilled', () => {
        expect(new Set(rows.map(([reason]) => reason)).size).toBe(rows.length);
        for (const [, sentence] of rows) expect(sentence).not.toMatch(/[{}]/);
        expect(rows.filter(([, sentence]) => sentence.includes('Frobnicate')).map(([reason]) => reason)).toEqual([
            'request.no_file_named',
            'request.unknown_tool',
        ]);
    });

    it('says nothing for a why it has no words for, or one not in the declared shape', () => {
        for (const value of [
            { reason: 'command.not_known_here' },
            { reason: 'request.unknown_tool' },
            { reason: 'request.unknown_tool', tool: '' },
            { reason: 'request.unknown_tool', tool: 7 },
            { reason: 'command.glob', tool: 'Bash' },
            { reason: 'command.glob', words: 'extra' },
            { reason: 'toString' },
            { reason: '__proto__' },
            { reason: 7 },
            'command.glob',
            null,
            [],
        ])
            expect(fullAccessWhyWords(value)).toBeNull();
    });

    it("keeps a card's why from the live event and from the kept record, and a card with none has none", () => {
        const live = received(newChat('c1', 'claude'), {
            kind: 'permission',
            request: 'req_1',
            title: 'Full access asks about this one',
            detail: 'ls *.md',
            remember: null,
            why: { reason: 'command.glob' },
        });
        expect(shown(live).at(-1)).toMatchObject({ kind: 'permission', why: { reason: 'command.glob' } });
        const kept = fromRecord(
            {
                id: 'conv_1',
                campaignId: null,
                title: 'Spring',
                updatedAt: '2026-11-11T10:00:00.000Z',
                running: false,
                mode: 'full_access',
                entries: [
                    {
                        kind: 'permission',
                        title: 'Full access asks about this one',
                        detail: 'x',
                        answer: 'deny',
                        why: { reason: 'request.unknown_tool', tool: 'Frobnicate' },
                    },
                    { kind: 'permission', title: 'Run a command', detail: 'git status', answer: 'deny' },
                ],
                dropped: 0,
                chosen: null,
            },
            'claude',
        );
        const cards = shown(kept).filter((entry) => entry.kind === 'permission');
        expect(cards.map((entry) => ('why' in entry ? entry.why : undefined))).toEqual([
            { reason: 'request.unknown_tool', tool: 'Frobnicate' },
            undefined,
        ]);
    });
});
