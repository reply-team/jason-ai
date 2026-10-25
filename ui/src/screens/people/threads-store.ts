import { signal, type Signal } from '@preact/signals';
import { ApiError, Unpaired, type Api } from '../../api/client';
import { session } from '../../api/session';
import type { ContactThread, ContactThreads, Message, Thread } from '../../api/types';

const threadsPage = 100;
const messagesPage = 20;

export type Failure = { text: string; retry: boolean };

/** A person's threads at one provider: a page at a time, when they were read, and why the last read failed. */
export type ThreadsState = {
    threads: Thread[] | null;
    next: number | null;
    readAt: Date | null;
    failure: Failure | null;
};

/** One thread's messages, a page at a time. */
export type MessagesState = { messages: Message[] | null; next: number | null; failure: Failure | null };

/**
 * What one section or one open thread shows, and the number of its latest read: an answer to an earlier read is
 * dropped, and so is one that arrives after the view is closed. Each view holds its own, so two views of one thread
 * never share what they show.
 */
export type View<S> = { state: Signal<S>; latest: number };

export function threadsView(): View<ThreadsState> {
    return { state: signal({ threads: null, next: null, readAt: null, failure: null }), latest: 0 };
}

export function messagesView(): View<MessagesState> {
    return { state: signal({ messages: null, next: null, failure: null }), latest: 0 };
}

/** A view closing: what is still on its way to it is dropped. */
export function closeView<S>(view: View<S>): void {
    view.latest++;
}

/** What a failed read says, and whether trying again can help: a read no route can reach will not be helped by it. */
function failureOf(error: unknown, pluginId: string): Failure | null {
    if (error instanceof Unpaired) {
        session.clear();
        return null;
    }
    if (!(error instanceof ApiError)) return { text: String(error), retry: true };
    if (error.code === 'no_route') {
        return {
            text: `These threads can't be read: no route reaches the ${pluginId} account this person was linked through.`,
            retry: false,
        };
    }
    return { text: error.message, retry: error.retryable };
}

/** The person's threads from `offset`; what is shown stays until the answer replaces it. */
export async function loadThreads(
    api: Api,
    view: View<ThreadsState>,
    contactId: string,
    pluginId: string,
    offset: number,
): Promise<void> {
    const state = view.state;
    const mine = ++view.latest;
    state.value = { ...state.value, failure: null };
    const body: Record<string, unknown> = { contact_id: contactId, plugin_id: pluginId, limit: threadsPage };
    if (offset > 0) body['offset'] = offset;
    try {
        const page = await api.call<ContactThreads>('contact.threads', body);
        if (view.latest !== mine) return;
        const known = offset > 0 ? (state.value.threads ?? []) : [];
        const seen = new Set(known.map((t) => t.external_id));
        state.value = {
            threads: [...known, ...page.items.filter((t) => !seen.has(t.external_id))],
            next: page.has_more && page.items.length > 0 ? offset + page.items.length : null,
            readAt: new Date(),
            failure: null,
        };
    } catch (error) {
        if (view.latest === mine) state.value = { ...state.value, failure: failureOf(error, pluginId) };
    }
}

/** A thread's messages from `offset`; the next page starts after what this one held, which a provider may make shorter. */
export async function loadMessages(
    api: Api,
    view: View<MessagesState>,
    contactId: string,
    pluginId: string,
    threadId: string,
    offset: number,
): Promise<void> {
    const state = view.state;
    const mine = ++view.latest;
    state.value = { ...state.value, failure: null };
    const body: Record<string, unknown> = {
        contact_id: contactId,
        plugin_id: pluginId,
        thread_id: threadId,
        limit: messagesPage,
    };
    if (offset > 0) body['offset'] = offset;
    try {
        const page = await api.call<ContactThread>('contact.thread', body);
        if (view.latest !== mine) return;
        state.value = {
            messages: offset > 0 ? [...(state.value.messages ?? []), ...page.messages] : page.messages,
            next: page.has_more && page.messages.length > 0 ? offset + page.messages.length : null,
            failure: null,
        };
    } catch (error) {
        if (view.latest === mine) state.value = { ...state.value, failure: failureOf(error, pluginId) };
    }
}
