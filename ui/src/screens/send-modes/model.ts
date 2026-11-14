import type { Api } from '../../api/client';
import type { ActorRef } from '../../api/types';
import { t } from '../../platform/i18n';
import type { PreferenceItem } from '../settings/voice-model';

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
