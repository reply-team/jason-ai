/**
 * The agent New GTM agent makes, in the desktop's words: the click's id as the page makes it, the number the next agent's
 * name takes, the actor the create is made under, and what a create that did not make the agent says. Pure; the chat
 * runs the CLI (`Chat.createAgent`).
 */

import type { CliResult } from './cli.ts';
import { usableAccount } from './chat-header.ts';

/** A click's id as the page makes it (`crypto.randomUUID()`): lower-case hex in the five groups of a UUID. */
export const clickIdShape = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;

/** The name the desktop gives an agent made at a click, before the person or the AI names it. */
export const agentName = (n: number): string => `New agent ${n}`;

/** A name the desktop gave at a click, exactly: its number from 1, written with no leading zero. */
const givenName = /^New agent ([1-9][0-9]{0,14})$/;

/**
 * The number of the next agent made at a click: one more than the highest any campaign carries in a name exactly as the
 * desktop gives it, archived ones included, else 1. A gap is never filled, a renamed agent no longer counts, and an
 * archived one still does, so a number is not handed out twice while an agent still carries it.
 */
export function nextAgentNumber(names: Iterable<string>): number {
    let highest = 0;
    for (const name of names) {
        const n = givenName.exec(name)?.[1];
        if (n !== undefined) highest = Math.max(highest, Number(n));
    }
    return highest + 1;
}

/** The key the create is made under: one per click, so a retry of the click is the same create. */
export const agentKey = (clickId: string): string => `desktop-agent:${clickId}`;

/**
 * The actor the create is made under: the person, by this account's name as the system gives it, where it can stand as
 * the header names the person (`chat-header.ts`); null where it cannot, and the CLI's own actor is left to stand.
 */
export function agentActor(account: unknown): string | null {
    return usableAccount(account) ? `human:${account}` : null;
}

/** What a click says while the runtime is not there to make the agent, or its list could not be read whole. */
export const agentRuntimeAway = 'Jason is not running. Start it, then try again.';

/** What a click its page made wrongly says: never a create. */
export const notAClick = 'That click is not one the desktop can make an agent for.';

/**
 * What a create that made no agent says: the CLI's own first line, the runtime's refusal's message where it printed one,
 * else its first diagnostic, after the desktop's words.
 */
export function agentRefused(answer: CliResult): string {
    let said = '';
    try {
        const body: unknown = JSON.parse(answer.stdout);
        const error = body !== null && typeof body === 'object' ? (body as { error?: { message?: unknown } }).error : undefined;
        if (typeof error?.message === 'string') said = error.message;
    } catch {
        // No body: the diagnostics say it.
    }
    if (said === '') said = [answer.stderr, answer.stdout].map((text) => text.split(/\r?\n/).find((line) => line.trim() !== '')?.trim() ?? '').find((line) => line !== '') ?? '';
    const line = said.replace(/[.\s]+$/, '');
    return line === '' ? 'Jason could not make the agent.' : `Jason could not make the agent: ${line}.`;
}
