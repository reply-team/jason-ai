import { useEffect, useMemo, useState } from 'react';
import { useApi } from '../../api/context';
import { Notice } from '../../layout/notice';
import { when } from '../../time/format';
import { channelName } from '../people/labels';
import { ThreadMessages } from '../people/threads';
import * as threadsStore from '../people/threads-store';
import { inboxLines, type InboxEntry } from './inbox-lines';

type Props = {
    /** The entries of the latest lines and how many people were left out, or none while they are on their way. */
    shown: { read: number; entries: InboxEntry[]; left: number } | null;
    /** The read failed; why is said above the tab, and nothing here says it is loading. */
    failed: boolean;
    zone: string;
    /** Reads the lines again, and with them every person's threads. */
    onRefresh: () => void;
};

/** One person's threads at the plugin their line names, read when the tab shows them. */
function PersonThreads({ contactId, pluginId, zone }: { contactId: string; pluginId: string; zone: string }) {
    const api = useApi();
    const view = useMemo(() => threadsStore.threadsView(), [contactId, pluginId]);
    const { threads, next, failure } = view.state.value;
    const [opened, setOpened] = useState<string | null>(null);
    const now = new Date();
    const read = (offset: number) => threadsStore.loadThreads(api, view, contactId, pluginId, offset);
    useEffect(() => {
        setOpened(null);
        void read(0);
        return () => threadsStore.closeView(view);
    }, [view]);

    return (
        <>
            {failure && (
                <Notice danger>
                    {failure.text}
                    {failure.retry && (
                        <>
                            {' '}
                            <button className="btn ghost" type="button" onClick={() => void read(0)}>
                                Try again
                            </button>
                        </>
                    )}
                </Notice>
            )}
            {threads === null && !failure && <div className="end loading">loading…</div>}
            {threads?.length === 0 && <div className="empty">No threads.</div>}
            {(threads ?? []).map((thread) => (
                <div key={thread.external_id} className="thread">
                    <button
                        type="button"
                        className="thread-head"
                        aria-expanded={opened === thread.external_id}
                        onClick={() => setOpened(opened === thread.external_id ? null : thread.external_id)}
                    >
                        <span className="ch">{thread.channel && channelName(thread.channel)}</span>
                        <span className="s">
                            {thread.subject}
                            {thread.subject && thread.preview && ' · '}
                            {thread.preview && <span className="pv">{thread.preview}</span>}
                        </span>
                        <span className="at">
                            {thread.unread && <span className="badge accent">unread</span>}
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
                <button className="btn ghost" type="button" onClick={() => void read(next)}>
                    Load more
                </button>
            )}
        </>
    );
}

/**
 * The Inbox: what the campaign's latest reply lines say, in the order Jason observed the replies. Each person is shown
 * with their threads asked of the plugin their line names; a reply the provider tied to nobody is its channel and
 * moment. Every moment is the provider's. What a thread says is the provider's, shown as text. Its Refresh is there
 * whatever the tab shows: on its way, empty, or failed.
 */
export function CampaignInbox({ shown, failed, zone, onRefresh }: Props) {
    const now = new Date();
    const entries = shown?.entries ?? [];
    return (
        <div className="campaign-tabs-inbox">
            <div className="campaign-tabs-inbox-head">
                <span className="hint">
                    {entries.length > 0 && 'The latest replies, in the order Jason observed them'}
                    {shown &&
                        shown.left > 0 &&
                        `; ${shown.left} more ${shown.left === 1 ? 'person' : 'people'} among the latest ${inboxLines} lines not shown here`}
                </span>
                <button className="btn ghost" type="button" aria-label="Refresh the inbox" onClick={onRefresh}>
                    Refresh
                </button>
            </div>
            {shown === null && !failed && <div className="end loading">loading…</div>}
            {shown !== null && entries.length === 0 && (
                <div className="empty">No reply to this agent has been observed yet.</div>
            )}
            {entries.map((entry, i) =>
                entry.kind === 'untied' ? (
                    <div key={`untied-${i}`} className="campaign-tabs-inbox-untied">
                        <span className="ch">{entry.channel && channelName(entry.channel)}</span>
                        <span className="s">A reply arrived; no person in Jason is tied to it</span>
                        <time>{when(new Date(entry.at), now, zone)}</time>
                    </div>
                ) : (
                    <div
                        key={`${entry.contactId}|${entry.pluginId ?? ''}`}
                        className="section threads campaign-tabs-inbox-person"
                    >
                        <div className="threads-head">
                            <h3>{entry.name ?? entry.contactId}</h3>
                            <span className="hint">
                                replied {when(new Date(entry.at), now, zone)}
                                {entry.pluginId && ` · at ${entry.pluginId}`}
                            </span>
                        </div>
                        {entry.pluginId ? (
                            <PersonThreads
                                key={shown?.read}
                                contactId={entry.contactId}
                                pluginId={entry.pluginId}
                                zone={zone}
                            />
                        ) : (
                            <div className="empty">The line names no plugin, so no threads are read.</div>
                        )}
                    </div>
                ),
            )}
        </div>
    );
}
