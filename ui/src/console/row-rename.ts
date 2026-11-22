import { signal } from '@preact/signals-react';
import { ApiError } from '../api/client';
import type { Campaign } from '../api/types';
import { t } from '../platform/i18n';
import { define, parseArgs } from './actions';
import { agentFacts } from './agent-facts';
import { campaigns } from './campaigns';
import { isRefusal, reloadConversations } from './chat';
import { changeChat, conversations, here, messageOf } from './state';

// Rename in the row of AGENTS or CHATS: the row's name becomes a field in place. An agent's row renames its campaign
// through `campaign.update`, with the person as actor, and the name the runtime keeps is the one the row, the agent's
// header and the Agents page then show; its folder is where it was. A chat's row renames its conversation through the
// desktop, which refuses in its own words. Enter keeps the name; Escape, and the field losing the focus, keep nothing; a
// refusal is said under the field, which stays open with what was typed. No chat is shown. One row at a time.

/** The row being renamed: its key, the agent it renames or the conversation, its name before, and what was refused. */
export interface RowRename {
    id: string;
    campaignId: string | null;
    conversationId: string | null;
    was: string;
    said: string | null;
    busy: boolean;
}

export const rowRename = signal<RowRename | null>(null);

/**
 * What the field holds now. Kept beside the signal, not in it, so typing paints nothing again; the field is drawn with it
 * whenever the row is painted.
 */
let typed = '';

/** The text the field is drawn with. */
export const typedName = (): string => typed;

/** The longest name a conversation keeps, as the desktop cuts it: sixty characters, never a character split in two. */
const chatNameLength = 60;

/** The row of a key, where it is drawn. */
function rowOf(id: string): HTMLElement | undefined {
    const rows = document.querySelectorAll<HTMLElement>('.side .recent-row');
    return [...rows].find((each) => parseArgs(each.dataset['args'])[0] === id);
}

/** The field, where it is drawn. */
const fieldOf = (): HTMLInputElement | null => document.querySelector<HTMLInputElement>('.side .rrename');

/** Opens the row's field with its name selected, so typing replaces it. */
export function beginRowRename(row: {
    id: string;
    title: string;
    campaignId: string | null;
    conversationId: string | null;
}): void {
    typed = row.title;
    rowRename.value = {
        id: row.id,
        campaignId: row.campaignId,
        conversationId: row.conversationId,
        was: row.title,
        said: null,
        busy: false,
    };
    const field = fieldOf();
    field?.focus();
    field?.select();
}

/** Closes the field and keeps nothing; the row takes the focus back where `focus` asks it. */
function close(focus: boolean): void {
    const at = rowRename.value;
    if (at === null) return;
    rowRename.value = null;
    if (focus) rowOf(at.id)?.focus();
}

/** Says a refusal under the field of the row, if that row's field is still open. */
function refused(id: string, said: string): void {
    const at = rowRename.value;
    if (at === null || at.id !== id) return;
    rowRename.value = { ...at, said, busy: false };
    fieldOf()?.focus();
}

/**
 * The runtime's refusal of a name: a name longer than it keeps, by the field and code its details name, in a person's
 * sentence; any other refusal in the runtime's own words.
 */
function refusalOfName(error: unknown): string {
    if (error instanceof ApiError && error.details?.some((each) => each.field === 'name' && each.code === 'too_long'))
        return t('side.rename.tooLong', { n: '200' });
    return messageOf(error);
}

/** Keeps the name typed: no request where it is unchanged, a refusal said in the row, and on success the row closes. */
async function keep(): Promise<void> {
    const at = rowRename.value;
    if (at === null || at.busy) return;
    const name = typed.trim();
    if (name === at.was.trim()) return close(true);
    if (name === '') return refused(at.id, t('side.rename.empty'));
    rowRename.value = { ...at, said: null, busy: true };
    if (at.campaignId !== null) {
        const id = at.campaignId;
        try {
            const answer = await here().api.call<Campaign>('campaign.update', { campaign_id: id, name }, 'write');
            // The name the runtime keeps is shown at once wherever the console holds the agent; the journal's line has the
            // list read again in its turn.
            campaigns.value = campaigns.value.map((each) => (each.id === id ? { ...each, name: answer.name } : each));
            const facts = agentFacts.value[id];
            if (facts !== undefined)
                agentFacts.value = {
                    ...agentFacts.value,
                    [id]: { ...facts, campaign: { ...facts.campaign, ...answer } },
                };
        } catch (error) {
            return refused(at.id, refusalOfName(error));
        }
    } else if (at.conversationId !== null) {
        const id = at.conversationId;
        try {
            const answer = await here().desktop.chat.rename(id, typed);
            if (isRefusal(answer)) return refused(at.id, answer.refused);
            changeChat(id, (chat) => ({ ...chat, title: answer.title }));
            conversations.value = conversations.value.map((each) =>
                each.id === id ? { ...each, title: answer.title } : each,
            );
            void reloadConversations();
        } catch (error) {
            return refused(at.id, messageOf(error));
        }
    }
    if (rowRename.value?.id === at.id) close(true);
}

define('side.renameType', (_args, element) => {
    if (!(element instanceof HTMLInputElement)) return;
    const at = rowRename.value;
    // A conversation's name is cut where the desktop would cut it, so the field never holds more than is kept.
    if (at !== null && at.campaignId === null) {
        const cut = Array.from(element.value).slice(0, chatNameLength).join('');
        if (cut !== element.value) element.value = cut;
    }
    typed = element.value;
});
define('side.renameKeep', () => void keep());
define('side.renameDrop', () => close(true));
define('side.renameBlur', () => {
    // Losing the focus keeps nothing, as Escape does; a field painted again keeps its focus and stays open.
    setTimeout(() => {
        const active = document.activeElement;
        if (active instanceof HTMLElement && active.classList.contains('rrename')) return;
        close(false);
    }, 0);
});
