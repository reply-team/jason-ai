// Pure: why a Full access card asks, by a closed code the page says in its own words (ui/src/locale/en.ts,
// `fullAccess.why.*`). Full access is the host's own bypass, and the turn's hook answers nothing but its refusals, so a
// card of a Full access turn is one the host asked about itself (`host_asked`). The other codes are those an earlier
// Full access held its requests by, which a record may still keep on a card, and which the page still says. The tool's
// name is the one part of a request a code may carry, for the two cases a card names its tool by.

import type { HeldReason } from './chat-literal.ts';

/** Every reason an earlier Full access held a shell command for, once: a reason added to `HeldReason` does not compile until it is here. */
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

/** The case of a request that was not a shell command, by Ask first's card for it, as an earlier Full access held it. */
export type RequestReason = (typeof requestReasons)[number];

/** The two cases a card names its tool by: a file tool given no file, and a tool Jason does not know. */
export type ToolReason = 'request.no_file_named' | 'request.unknown_tool';

/**
 * Why a Full access card asks: `host_asked`, the host asked about it itself; a kept card from before may carry a shell
 * command's closed reason, or Ask first's card for any other request, as an earlier Full access held it.
 */
export type FullAccessWhy =
    | { reason: `command.${HeldReason}` | RequestReason | 'host_asked' }
    | { reason: ToolReason; tool: string };

/** A tool's name as a card may carry it: the host's own spelling of a name, never anything else of the request. */
const toolShape = /^[A-Za-z0-9_.:-]{1,128}$/;

/** The why of every card a Full access turn shows: the host asked about it itself. */
export const hostAsked: FullAccessWhy = { reason: 'host_asked' };

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
