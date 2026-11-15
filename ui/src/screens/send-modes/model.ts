import type { Api } from '../../api/client';
import type { ActorRef, ModeInForce } from '../../api/types';
import type { MessageKey } from '../../locale/en';
import { t } from '../../platform/i18n';
import { senderKey, toneKey, valueOf, type PreferenceItem, type PreferenceUnsetRequest } from '../settings/voice-model';

// What the send-mode words and acts of the page share: the two preferences, the two modes, where a mode comes from,
// and the doubts an approval's preview carries. The words themselves are in the locale table.

export const coldKey = 'send_mode.cold';
export const warmKey = 'send_mode.warm';
export const kinds = ['cold', 'warm'] as const;
export type Kind = (typeof kinds)[number];
export const keyOf = (kind: Kind): string => (kind === 'cold' ? coldKey : warmKey);

export const modes = ['every_message', 'automatic'] as const;
export type Mode = (typeof modes)[number];

/** The reason a preference answer carries: the person's own act on the card, said once. */
export const answerReason = "the person's answer";

export type SendModePreferenceRequest = { key: string; value: { mode: Mode }; actor?: ActorRef; reason?: string };

/** The word of a mode this build knows, or null for one it does not: a newer runtime's word is shown as it is. */
export const knownMode = (word: string): Mode | null => modes.find((each) => each === word) ?? null;

export const modeWords = (word: string): { name: string; note: string | null } => {
    const known = knownMode(word);
    return known === null ? { name: word, note: null } : { name: t(`modes.${known}`), note: t(`modes.${known}.note`) };
};

/** The unset keys this build asks about, in the order of the runtime's list; a key of another kind is left alone. */
export const askedKinds = (unset: readonly string[] | undefined): Kind[] =>
    kinds.filter((kind) => (unset ?? []).includes(keyOf(kind)));

/** Sets one of the two preferences as the paired person: the card's "Remember this". */
export async function remember(api: Api, kind: Kind, mode: Mode): Promise<void> {
    await api.call<PreferenceItem>(
        'preference.set',
        { key: keyOf(kind), value: { mode }, reason: answerReason } satisfies SendModePreferenceRequest,
        'write',
    );
}

/** Gives one of the two preferences back as the paired person: unanswered again, so every message waits and is asked. */
export async function forget(api: Api, kind: Kind): Promise<void> {
    await api.call<PreferenceItem>(
        'preference.unset',
        { key: keyOf(kind), reason: answerReason } satisfies PreferenceUnsetRequest,
        'write',
    );
}

/** The facts the claim decided a send on, as the approval's preview carries them under `send_mode`; each null where unsaid. */
export type SendModeFacts = {
    mode: string | null;
    source: string | null;
    temperature: string | null;
    review: string | null;
};

/** The preview's send-mode facts, or null where it carries none or not in the runtime's shape. */
export function sendModeOf(preview: Record<string, unknown>): SendModeFacts | null {
    const facts = preview['send_mode'];
    if (!isObject(facts)) return null;
    const word = (key: string): string | null => (typeof facts[key] === 'string' ? facts[key] : null);
    return { mode: word('mode'), source: word('source'), temperature: word('temperature'), review: word('review') };
}

export type Doubt = { source: string; who: string | null; role: string | null; code: string; note: string | null };

const isObject = (value: unknown): value is Record<string, unknown> =>
    typeof value === 'object' && value !== null && !Array.isArray(value);

/**
 * The doubts an approval's preview carries, in the order it lists them. An entry that is not the runtime's shape is
 * dropped, never guessed at; a preview with none yields none.
 */
export function doubtsOf(preview: Record<string, unknown>): Doubt[] {
    const list = preview['doubts'];
    if (!Array.isArray(list)) return [];
    return list.flatMap((entry: unknown) => {
        if (!isObject(entry) || typeof entry['code'] !== 'string' || typeof entry['source'] !== 'string') return [];
        const by = isObject(entry['by']) ? entry['by'] : {};
        return [
            {
                source: entry['source'],
                who: by['type'] === 'human' ? (typeof by['id'] === 'string' ? by['id'] : '') : null,
                role: typeof entry['role'] === 'string' ? entry['role'] : null,
                code: entry['code'],
                note: typeof entry['note'] === 'string' && entry['note'] !== '' ? entry['note'] : null,
            },
        ];
    });
}

const knownCodes = [
    'unsupported_claim',
    'not_in_memory',
    'off_brief',
    'sensitive_topic',
    'instruction_in_text',
    'new_kind_of_person',
    'wrong_recipient',
    'other',
];

/** Why a send is doubted, in words; a code this build does not know is shown as it is. */
export const whyWords = (code: string): string =>
    knownCodes.includes(code) ? t(`modes.code.${code}` as MessageKey) : code;

function whoWords(doubt: Doubt): string {
    if (doubt.who) return doubt.who;
    if (doubt.role) return t('modes.doubts.who.role', { role: doubt.role });
    return doubt.who === '' ? t('modes.doubts.who.person') : t('modes.doubts.who.ai');
}

/** One doubt in words: who doubted the send and why, and the sentence the doubter wrote, when they wrote one. */
export type DoubtWords = { said: string; note: string | null };

/**
 * The doubts an approval's preview carries, in the words every place that draws them uses: a heading and, for each
 * doubt, who doubted the send and why, and the doubter's own sentence. Null where the preview carries none. Text only:
 * each place draws it as text, never as markup.
 */
export function doubtWords(preview: Record<string, unknown>): { heading: string; doubts: DoubtWords[] } | null {
    const doubts = doubtsOf(preview);
    if (doubts.length === 0) return null;
    return {
        heading: t('modes.doubts.heading'),
        doubts: doubts.map((doubt) => ({
            said: t(doubt.source === 'ask_person' ? 'modes.doubts.asked' : 'modes.doubts.reviewed', {
                who: whoWords(doubt),
                why: whyWords(doubt.code),
            }),
            note: doubt.note ? t('modes.doubts.note', { note: doubt.note }) : null,
        })),
    };
}

/** The mode word a stored send-mode preference holds, or null for one unset, which the runtime treats as every message. */
export function storedMode(items: readonly PreferenceItem[], kind: Kind): string | null {
    const value = valueOf(items, keyOf(kind));
    return isObject(value) && typeof value['mode'] === 'string' ? value['mode'] : null;
}

/**
 * Whether the reminder about name and tone is due: while sends wait for the person, never. It is due only while a mode
 * in force goes automatically, the installation's own or that of the agent in front of the person, and the name or the
 * tone is unset; once both are set it is not.
 */
export function remindsOfVoice(
    items: readonly PreferenceItem[],
    agent: { cold: ModeInForce; warm: ModeInForce } | undefined,
): boolean {
    if (!voiceUnset(items)) return false;
    return kinds.some((kind) => storedMode(items, kind) === 'automatic' || agent?.[kind].mode === 'automatic');
}

/** Whether the name or the tone, which messages are written from, is unset. */
export const voiceUnset = (items: readonly PreferenceItem[]): boolean =>
    valueOf(items, senderKey) === null || valueOf(items, toneKey) === null;

/** Whether the installation's own answers say enough without the agent: a kind of it goes automatically. */
export const installationAutomatic = (items: readonly PreferenceItem[]): boolean =>
    kinds.some((kind) => storedMode(items, kind) === 'automatic');
