import { useEffect, useState } from 'preact/hooks';
import { ApiError, Unpaired } from '../../api/client';
import { useApi } from '../../api/context';
import { session } from '../../api/session';
import type { Contact, ContactThread, ContactThreads, Message, Thread } from '../../api/types';
import { clock, when } from '../../time/format';
import { channelName } from './labels';
import { MessageBody } from './message-body';

const threadsPage = 100;
const messagesPage = 20;

type Failure = { text: string; retry: boolean };

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

function Failed({ failure, retry }: { failure: Failure; retry: () => void }) {
    return (
        <div class="notice danger" role="alert">
            {failure.text}
            {failure.retry && (
                <>
                    {' '}
                    <button class="btn ghost" type="button" onClick={retry}>
                        Try again
                    </button>
                </>
            )}
        </div>
    );
}

/**
 * One thread's messages, read from the provider when it is opened and kept only while it stays open. Each body is
 * cleaned before it is inserted.
 */
export function ThreadMessages({
    contactId,
    pluginId,
    threadId,
    zone,
}: {
    contactId: string;
    pluginId: string;
    threadId: string;
    zone: string;
}) {
    const api = useApi();
    const [messages, setMessages] = useState<Message[] | null>(null);
    const [next, setNext] = useState<number | null>(null);
    const [failure, setFailure] = useState<Failure | null>(null);
    const now = new Date();

    const read = async (offset: number) => {
        setFailure(null);
        const body: Record<string, unknown> = {
            contact_id: contactId,
            plugin_id: pluginId,
            thread_id: threadId,
            limit: messagesPage,
        };
        if (offset > 0) body['offset'] = offset;
        try {
            const page = await api.call<ContactThread>('contact.thread', body);
            setMessages((before) => (offset > 0 ? [...(before ?? []), ...page.messages] : page.messages));
            // The next page starts after what this one held, which a provider may make shorter than was asked.
            setNext(page.has_more && page.messages.length > 0 ? offset + page.messages.length : null);
        } catch (error) {
            setFailure(failureOf(error, pluginId));
        }
    };
    useEffect(() => {
        void read(0);
    }, [contactId, pluginId, threadId]);

    return (
        <div class="msgs">
            {failure && <Failed failure={failure} retry={() => void read(messages === null ? 0 : (next ?? 0))} />}
            {messages === null && !failure && <div class="end loading">loading…</div>}
            {(messages ?? []).map((message, at) => {
                const to = (message.to ?? []).join(', ');
                return (
                    <div
                        key={message.external_id ?? at}
                        class={`msg ${message.direction === 'inbound' ? 'in' : 'out'}`}
                    >
                        <div class="who">
                            <span>
                                {message.from ?? '?'}
                                {to && ` → ${to}`}
                            </span>
                            {message.sent_at && <time>{when(new Date(message.sent_at), now, zone)}</time>}
                        </div>
                        <MessageBody body={message.body ?? ''} />
                    </div>
                );
            })}
            {next !== null && (
                <button class="btn ghost" type="button" onClick={() => void read(next)}>
                    Load more
                </button>
            )}
        </div>
    );
}

/** The person's threads at one provider, read when they are shown and kept only while they are. */
function Threads({ contactId, pluginId, zone }: { contactId: string; pluginId: string; zone: string }) {
    const api = useApi();
    const [threads, setThreads] = useState<Thread[] | null>(null);
    const [next, setNext] = useState<number | null>(null);
    const [readAt, setReadAt] = useState<Date | null>(null);
    const [failure, setFailure] = useState<Failure | null>(null);
    const [opened, setOpened] = useState<string | null>(null);
    const now = new Date();

    const read = async (offset: number) => {
        setFailure(null);
        const body: Record<string, unknown> = { contact_id: contactId, plugin_id: pluginId, limit: threadsPage };
        if (offset > 0) body['offset'] = offset;
        try {
            const page = await api.call<ContactThreads>('contact.threads', body);
            setThreads((before) => {
                const known = offset > 0 ? (before ?? []) : [];
                const seen = new Set(known.map((t) => t.external_id));
                return [...known, ...page.items.filter((t) => !seen.has(t.external_id))];
            });
            setNext(page.has_more && page.items.length > 0 ? offset + page.items.length : null);
            setReadAt(new Date());
        } catch (error) {
            setFailure(failureOf(error, pluginId));
        }
    };
    useEffect(() => {
        setThreads(null);
        setOpened(null);
        void read(0);
    }, [contactId, pluginId]);

    return (
        <div class="section threads">
            <div class="threads-head">
                <h3>At {pluginId}</h3>
                {readAt && <span class="hint">read at {clock(readAt, zone)}</span>}
                <span class="spacer" />
                {threads !== null && (
                    <button class="btn ghost" type="button" onClick={() => void read(0)}>
                        Refresh
                    </button>
                )}
            </div>
            {failure && <Failed failure={failure} retry={() => void read(0)} />}
            {threads === null && !failure && <div class="end loading">loading…</div>}
            {threads?.length === 0 && <div class="empty">No threads.</div>}
            {(threads ?? []).map((thread) => (
                <div key={thread.external_id} class="thread">
                    <button
                        type="button"
                        class="thread-head"
                        aria-expanded={opened === thread.external_id}
                        onClick={() => setOpened(opened === thread.external_id ? null : thread.external_id)}
                    >
                        <span class="ch">{thread.channel && channelName(thread.channel)}</span>
                        <span class="s">
                            {thread.subject}
                            {thread.subject && thread.preview && ' · '}
                            {thread.preview && <span class="pv">{thread.preview}</span>}
                        </span>
                        <span class="at">
                            {thread.unread && <span class="badge accent">unread</span>}
                            {thread.last_activity_at && (
                                <time>{when(new Date(thread.last_activity_at), now, zone)}</time>
                            )}
                        </span>
                    </button>
                    {opened === thread.external_id && (
                        <ThreadMessages
                            contactId={contactId}
                            pluginId={pluginId}
                            threadId={thread.external_id}
                            zone={zone}
                        />
                    )}
                </div>
            ))}
            {next !== null && (
                <button class="btn ghost" type="button" onClick={() => void read(next)}>
                    Load more
                </button>
            )}
        </div>
    );
}

/** Whether the person is linked at a provider that can be asked for their threads: the tab shows only then. */
export function linked(person: Contact): boolean {
    return (person.external_ids ?? []).some((pin) => pin.kind === 'contact');
}

/** The Threads tab: the person's threads at each provider they are linked at. */
export function ThreadsTab({ person, zone }: { person: Contact; zone: string }) {
    return (
        <div class="panel-body">
            {(person.external_ids ?? [])
                .filter((pin) => pin.kind === 'contact')
                .map((pin) => (
                    <Threads key={pin.plugin_id} contactId={person.id} pluginId={pin.plugin_id} zone={zone} />
                ))}
        </div>
    );
}
