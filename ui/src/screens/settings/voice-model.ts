import type { Api } from '../../api/client';
import type { ActorRef, Page } from '../../api/types';

// The two named preferences a person states about themselves: who messages are written as (`sender.identity`) and how
// they sound (`tone_of_voice`). The form shows a few of their fields; a save changes those and writes the rest back as
// the runtime holds it, so nothing the form does not show is ever dropped.

export type PreferenceItem = {
    key: string;
    value: unknown;
    value_hash: string | null;
    value_bytes: number | null;
    created_at: string | null;
    updated_at: string | null;
    updated_by: ActorRef | null;
};

export type PreferenceSetRequest = { key: string; value: object; actor?: ActorRef; reason?: string };

export const senderKey = 'sender.identity';
export const toneKey = 'tone_of_voice';

export type GenderForms = 'feminine' | 'masculine' | 'neutral';
export type Formality = 'informal' | 'neutral' | 'formal';

export const genderForms: readonly GenderForms[] = ['feminine', 'masculine', 'neutral'];
export const formalities: readonly Formality[] = ['informal', 'neutral', 'formal'];

/** What the "Who writes" part shows. */
export type SenderForm = { name: string; title: string; company: string; gender: GenderForms };
/** What the "How you write" part shows; an empty formality is none stated. */
export type ToneForm = { summary: string; formality: Formality | '' };

export const emptySender: SenderForm = { name: '', title: '', company: '', gender: 'neutral' };
export const emptyTone: ToneForm = { summary: '', formality: '' };

const isObject = (value: unknown): value is Record<string, unknown> =>
    typeof value === 'object' && value !== null && !Array.isArray(value);

const text = (value: unknown): string => (typeof value === 'string' ? value : '');

/** Thrown when a stored value is not a shape the form can write back without losing something. */
export class Unreadable extends Error {}

/** The senders a stored value holds; none for a preference not set. */
function sendersOf(value: unknown): Record<string, unknown>[] {
    if (value === null || value === undefined) return [];
    if (!isObject(value) || !Array.isArray(value['senders']) || !value['senders'].every(isObject))
        throw new Unreadable();
    return value['senders'];
}

/** How many senders a stored value holds. */
export function senderCount(value: unknown): number {
    try {
        return sendersOf(value).length;
    } catch {
        return 0;
    }
}

/** The form's values from a stored sender identity: the first sender's. */
export function senderForm(value: unknown): SenderForm {
    let first: Record<string, unknown> | undefined;
    try {
        first = sendersOf(value)[0];
    } catch {
        return emptySender;
    }
    if (!first) return emptySender;
    const gender = genderForms.find((each) => each === first['gender_forms']);
    return {
        name: text(first['name']),
        title: text(first['title']),
        company: text(first['company']),
        gender: gender ?? 'neutral',
    };
}

/** The form's values from a stored tone of voice. */
export function toneForm(value: unknown): ToneForm {
    if (!isObject(value)) return emptyTone;
    const formality = formalities.find((each) => each === value['formality']);
    return { summary: text(value['summary']), formality: formality ?? '' };
}

/**
 * Sets a text member only where the person changed it: a field equal to what the form painted is left as the stored
 * value holds it, so a member set elsewhere since is never taken away. A field changed to nothing takes the member away.
 */
function put(into: Record<string, unknown>, key: string, now: string, painted: string): void {
    if (now === painted) return;
    const trimmed = now.trim();
    if (trimmed === '') delete into[key];
    else into[key] = trimmed;
}

/**
 * The whole value to write for the sender part: the stored value with only what the person changed in the first
 * sender's name, title, company and grammar forms replaced; what the schema requires and the value lacks (the name, the
 * grammar forms) is written either way. Every other member of that sender, every further sender and every other member
 * of the value stays as it was read. Null when there is no name to write: nothing is saved.
 */
export function mergeSender(current: unknown, form: SenderForm, painted: SenderForm = emptySender): object | null {
    const senders = sendersOf(current);
    // A name is required, so an emptied one has nothing to save, whatever is stored.
    if (form.name.trim() === '') return null;
    const first: Record<string, unknown> = { ...(senders[0] ?? {}) };
    if (form.name !== painted.name || text(first['name']) === '') first['name'] = form.name.trim();
    put(first, 'title', form.title, painted.title);
    put(first, 'company', form.company, painted.company);
    if (form.gender !== painted.gender || !genderForms.some((each) => each === first['gender_forms'])) {
        first['gender_forms'] = form.gender;
    }
    return { ...(isObject(current) ? current : {}), senders: [first, ...senders.slice(1)] };
}

/**
 * The whole value to write for the tone part: the stored value with only what the person changed in the summary and the
 * formality replaced; the greeting, the sign-off and the examples stay. Null when there is no summary to write: a summary
 * is never made up.
 */
export function mergeTone(current: unknown, form: ToneForm, painted: ToneForm = emptyTone): object | null {
    if (current !== null && current !== undefined && !isObject(current)) throw new Unreadable();
    // A summary is required and never made up, so an emptied one has nothing to save, whatever is stored.
    if (form.summary.trim() === '') return null;
    const value: Record<string, unknown> = { ...(isObject(current) ? current : {}) };
    if (form.summary !== painted.summary || text(value['summary']) === '') value['summary'] = form.summary.trim();
    if (form.formality !== painted.formality) {
        if (form.formality === '') delete value['formality'];
        else value['formality'] = form.formality;
    }
    return value;
}

/** Every key the runtime knows, read now: a save starts from what is stored at that moment, never from what was shown. */
export async function readPreferences(api: Api): Promise<PreferenceItem[]> {
    const items: PreferenceItem[] = [];
    let cursor: string | null = null;
    do {
        const page: Page<PreferenceItem> = await api.call<Page<PreferenceItem>>(
            'preference.list',
            cursor ? { cursor } : {},
        );
        items.push(...page.items);
        cursor = page.next_cursor;
    } while (cursor);
    return items;
}

export const valueOf = (items: readonly PreferenceItem[], key: string): unknown =>
    items.find((each) => each.key === key)?.value ?? null;

/** Whether a read holds neither of the two preferences. */
export const noneStated = (items: readonly PreferenceItem[]): boolean =>
    valueOf(items, senderKey) === null && valueOf(items, toneKey) === null;

/**
 * Saves one part: reads the preferences again, changes only what the person changed from what the form painted, and
 * writes the whole value back. Resolves to the value written, or null when the form holds nothing to save (no name or no
 * summary to write); it never writes then.
 */
export async function saveSender(api: Api, form: SenderForm, painted: SenderForm): Promise<object | null> {
    const items = await readPreferences(api);
    const value = mergeSender(valueOf(items, senderKey), form, painted);
    if (value === null) return null;
    await api.call<PreferenceItem>('preference.set', { key: senderKey, value } satisfies PreferenceSetRequest, 'write');
    return value;
}

/** Saves the tone part, as `saveSender` saves the other. */
export async function saveTone(api: Api, form: ToneForm, painted: ToneForm): Promise<object | null> {
    const items = await readPreferences(api);
    const value = mergeTone(valueOf(items, toneKey), form, painted);
    if (value === null) return null;
    await api.call<PreferenceItem>('preference.set', { key: toneKey, value } satisfies PreferenceSetRequest, 'write');
    return value;
}
