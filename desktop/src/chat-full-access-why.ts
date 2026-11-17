// Pure: why a Full access turn still puts a request on a card. It reads the two judgements the broker already made, Full
// access's and Ask first's, and names the case by a closed code; it changes neither and decides nothing. The page says
// each code in its own words (ui/src/locale/en.ts, `fullAccess.why.*`). The tool's name is the one part of a request a
// code may carry, for the two cases a card names its tool by; nothing else of the request goes with it.

import type { HeldReason } from './chat-literal.ts';
import { personsToolTitle, serversTitle, type FullAccessJudgement, type Judgement } from './chat-permission-model.ts';

/** Every reason a shell command is held for, once: a reason added to `HeldReason` does not compile until it is here. */
const commandReasons = {
    expansion: true,
    escape: true,
    glob: true,
    brace: true,
    group: true,
    history: true,
    comment: true,
    tilde: true,
    non_ascii: true,
    control_character: true,
    character: true,
    operator: true,
    background: true,
    assignment: true,
    here_document: true,
    process_substitution: true,
    quote: true,
    array: true,
    stop_parsing: true,
    call_operator: true,
    syntax: true,
    unknown_shell: true,
    command_prompt: true,
    inline_code: true,
    runner: true,
    builtin: true,
    servers: true,
    directory_change: true,
    reply: true,
    guarded_name: true,
    search: true,
    local_jason: true,
    holds_data: true,
    link: true,
} as const satisfies Record<HeldReason, true>;

/**
 * Ask first's card titles that Full access holds rather than runs, each with the code of its case. Every title Ask first
 * can give is either one of these, one of the cards Full access runs, or one that names its tool (below); a test holds
 * the titles the permission model writes to that.
 */
export const requestTitles: ReadonlyMap<string, RequestReason> = new Map<string, RequestReason>([
    [personsToolTitle, 'request.own_tool'],
    ['Change the AI\'s own settings', 'request.settings'],
    ['Change a repository\'s own files', 'request.repository'],
    ['Change a folder\'s Jason marker', 'request.marker'],
    ['Change the skills or settings the AI loads', 'request.loads'],
    ['Change instructions the AI reads', 'request.instructions'],
    [serversTitle, 'request.server_list'],
    ['Write or run a jason that is not the desktop\'s', 'request.local_jason'],
    ['Change where the shell finds programs', 'request.search'],
    ['Use a path Windows reads as another name', 'request.windows_name'],
    ['Use a path that begins with ~', 'request.tilde_path'],
]);

const requestReasons = [
    'request.own_tool',
    'request.settings',
    'request.repository',
    'request.marker',
    'request.loads',
    'request.instructions',
    'request.server_list',
    'request.local_jason',
    'request.search',
    'request.windows_name',
    'request.tilde_path',
    'request.other',
] as const;

/** The case of a request that is not a shell command, by Ask first's card for it. */
export type RequestReason = (typeof requestReasons)[number];

/** The two cases a card names its tool by: a file tool given no file, and a tool Jason does not know. */
export type ToolReason = 'request.no_file_named' | 'request.unknown_tool';

/**
 * Why Full access asks about one request: a shell command by the closed reason it was held for, any other request by
 * Ask first's card for it, and `host_asked` where Full access would have run it and the host asked all the same.
 */
export type FullAccessWhy =
    | { reason: `command.${HeldReason}` | RequestReason | 'host_asked' }
    | { reason: ToolReason; tool: string };

/** A tool's name as a card may carry it: the host's own spelling of a name, never anything else of the request. */
const toolShape = /^[A-Za-z0-9_.:-]{1,128}$/;

/**
 * Why the card of a Full access turn asks: `full` is Full access's judgement of the request and `judged` Ask first's
 * card for it, both as the broker made them. Held with a reason, a shell command; held with none, Ask first's card says
 * which case; run by Full access and asked about all the same, the host asked.
 */
export function fullAccessWhy(full: FullAccessJudgement, judged: Extract<Judgement, { verdict: 'ask' }>, tool: string): FullAccessWhy {
    if (full.outcome === 'runs') return { reason: 'host_asked' };
    if (full.outcome === 'held' && full.reason !== undefined) return { reason: `command.${full.reason}` };
    const titled = requestTitles.get(judged.title);
    if (titled !== undefined) return { reason: titled };
    if (toolShape.test(tool)) {
        if (judged.title === `Use ${tool} with no file named`) return { reason: 'request.no_file_named', tool };
        if (judged.title === `Use ${tool}`) return { reason: 'request.unknown_tool', tool };
    }
    return { reason: 'request.other' };
}

/** Whether a kept card's `why` is one the table can say: a known code, and a tool's name only where its code takes one. */
export function isFullAccessWhy(value: unknown): value is FullAccessWhy {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
    const { reason, tool, ...rest } = value as Record<string, unknown>;
    if (Object.keys(rest).length > 0 || typeof reason !== 'string') return false;
    if (reason === 'request.no_file_named' || reason === 'request.unknown_tool') return typeof tool === 'string' && toolShape.test(tool);
    if (tool !== undefined) return false;
    if (reason === 'host_asked' || (requestReasons as readonly string[]).includes(reason)) return true;
    return reason.startsWith('command.') && Object.hasOwn(commandReasons, reason.slice('command.'.length));
}

/** Every code the page has words for, the two that name a tool among them. */
export const fullAccessReasons: readonly FullAccessWhy['reason'][] = [
    ...Object.keys(commandReasons).map((reason) => `command.${reason as HeldReason}` as const),
    ...requestReasons,
    'request.no_file_named',
    'request.unknown_tool',
    'host_asked',
];
