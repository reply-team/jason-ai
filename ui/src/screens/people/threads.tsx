import { useEffect, useMemo, useState } from 'preact/hooks';
import * as threadsStore from './threads-store';
import type { Failure } from './threads-store';
import { useApi } from '../../api/context';
import type { Contact } from '../../api/types';
import { clock, when } from '../../time/format';
import { channelName } from './labels';
import { MessageBody } from './message-body';
import { Notice } from '../../layout/notice';

function Failed({ failure, retry }: { failure: Failure; retry: () => void }) {
    return (
        <Notice danger>
            {failure.text}
            {failure.retry && (
                <>
                    {' '}
                    <button class="btn ghost" type="button" onClick={retry}>
                        Try again
                    </button>
                </>
            )}
        </Notice>
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
    const view = useMemo(() => threadsStore.messagesView(), [contactId, pluginId, threadId]);
    const { messages, next, failure } = view.state.value;
    const now = new Date();
    const read = (offset: number) => threadsStore.loadMessages(api, view, contactId, pluginId, threadId, offset);
    useEffect(() => {
        void read(0);
        return () => threadsStore.closeView(view);
    }, [view]);

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
    const view = useMemo(() => threadsStore.threadsView(), [contactId, pluginId]);
    const { threads, next, readAt, failure } = view.state.value;
    const [opened, setOpened] = useState<string | null>(null);
    const now = new Date();
    const read = (offset: number) => threadsStore.loadThreads(api, view, contactId, pluginId, offset);
    useEffect(() => {
        setOpened(null);
        void read(0);
        return () => threadsStore.closeView(view);
    }, [view]);

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
