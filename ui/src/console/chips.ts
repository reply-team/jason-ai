import type { CampaignSummaryRow } from '../api/types';
import { foldLabel, openChoice, placeOf } from './cards';
import type { Chat, Entry } from './chat-model';

// The chips a chat offers: on an empty chat its opening three, and under the last answer up to three questions the
// person might ask next. In an agent's chat they are chosen from the campaign's counts as the runtime keeps them now
// (`campaign.summary`, the row the console already reads for every agent it lists), so they follow what is on record and
// never what the AI said. A chip's words are the person's question, never a claim, and hold no number; a click sends
// them as the person's next message and does nothing else, so every word a chip shows is one that acts on nothing
// (`passiveWords`). A chip shows the page's word for a campaign, "agent", and sends the runtime's, "campaign": the AI's
// own rules say campaign.

export interface Chip {
    shown: string;
    sent: string;
}

const chip = (shown: string, sent = shown): Chip => ({ shown, sent });

/** The chips of an empty chat, and under an answer in the Feed and in a set-up chat. */
export const openingChips: readonly Chip[] = [
    chip('What is waiting for me?'),
    chip('How are my agents doing?', 'How are my campaigns doing?'),
    chip('Help me start a new agent.', 'Help me start a new campaign.'),
];

/** An agent with nobody in it. */
const nobody: readonly Chip[] = [
    chip('Who should this agent reach?', 'Who should this campaign reach?'),
    chip('Show me the plan'),
    chip('What can you do here?'),
];
/** Someone in the agent has an approval or a question waiting for the person. */
const waiting = chip('What is waiting for me?');
/** People in the agent, none of whom anything was done or planned for yet. */
const unstarted: readonly Chip[] = [chip('Show me the people'), chip('What happens when it starts?')];
/** People in the agent, someone of whom was worked on. */
const underway: readonly Chip[] = [chip('How is it going?'), chip('Show me the people')];

/** Every word a chip shows, once each: the words of buttons that act on nothing. */
export const chipWords: readonly string[] = [
    ...new Set([...openingChips, ...nobody, waiting, ...unstarted, ...underway].map((each) => each.shown)),
];

/** The chips of an agent by its counts now: who to reach while nobody is in it; else what waits first, then the rest. */
function agentChips(row: CampaignSummaryRow): readonly Chip[] {
    if (row.people === 0) return nobody;
    // `new` counts the people nothing was done or planned for; any other count is someone worked on.
    const rest = row.new >= row.people ? unstarted : underway;
    return row.waiting > 0 ? [waiting, ...rest] : rest;
}

/** The latest entry that holds to a test, if any. */
function latest(entries: readonly Entry[], test: (entry: Entry) => boolean): Entry | undefined {
    for (let at = entries.length - 1; at >= 0; at--) if (test(entries[at]!)) return entries[at];
    return undefined;
}

/** The most chips under an answer. */
const most = 3;

/**
 * The chips under a chat's last answer. None while a turn runs; none on an empty chat, whose opening draws its own; none
 * when the conversation's last entry (the Feed's lines and gaps aside) is anything but the AI's words, a plan the
 * desktop marked included; none while a choice stands open. Otherwise an agent's by its counts (none while they are
 * unread), and the opening three in the Feed and a set-up chat; never one that asks the person's last message again, as
 * a person reads it.
 */
export function chipsUnder(
    chat: Pick<Chat, 'entries' | 'campaignId' | 'main' | 'running' | 'reopened'>,
    row: CampaignSummaryRow | undefined,
): readonly Chip[] {
    if (chat.running) return [];
    const last = latest(chat.entries, (entry) => entry.kind !== 'line' && entry.kind !== 'gap');
    if (last?.kind !== 'text' || (last.plan === true && !chat.main)) return [];
    if (openChoice(chat) !== null) return [];
    const offered = placeOf(chat) !== 'agent' ? openingChips : row === undefined ? [] : agentChips(row);
    const said = latest(chat.entries, (entry) => entry.kind === 'user');
    const asked = said?.kind === 'user' ? foldLabel(said.text) : null;
    return offered
        .filter((each) => asked === null || (foldLabel(each.shown) !== asked && foldLabel(each.sent) !== asked))
        .slice(0, most);
}
