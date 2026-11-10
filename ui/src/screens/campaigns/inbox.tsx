import { useEffect, useMemo, useState } from 'react';
import { useApi } from '../../api/context';
import { Notice } from '../../layout/notice';
import { when } from '../../time/format';
import { channelName } from '../people/labels';
import { ThreadMessages } from '../people/threads';
import * as threadsStore from '../people/threads-store';
import type { InboxPerson } from './store';

type Props = {
    /** The people and the route's plugin, or none while they are on their way. */
    shown: { people: InboxPerson[]; pluginId: string | null } | null;
    zone: string;
};

/** One person's threads at the plugin the campaign's route reads conversations through, read when the tab shows them. */
function PersonThreads({ person, pluginId, zone }: { person: InboxPerson; pluginId: string; zone: string }) {
    const api = useApi();
    const view = useMemo(() => threadsStore.threadsView(), [person.contactId, pluginId]);
    const { threads, next, failure } = view.state.value;
    const [opened, setOpened] = useState<string | null>(null);
    const now = new Date();
    const read = (offset: number) => threadsStore.loadThreads(api, view, person.contactId, pluginId, offset);
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
                            contactId={person.contactId}
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
 * The Inbox: everyone a reply was observed for in the campaign, as its journal says, each with their threads read
 * through the plugin the campaign's route names. What a thread says is the provider's, shown as text.
 */
export function CampaignInbox({ shown, zone }: Props) {
    const now = new Date();
    if (shown === null) return <div className="end loading">loading…</div>;
    if (shown.people.length === 0) return <div className="empty">No reply has been observed in this campaign yet.</div>;
    return (
        <div className="campaign-tabs-inbox">
            {shown.pluginId === null && (
                <Notice status>
                    No route reads this campaign’s conversations, so their threads can’t be read here.
                </Notice>
            )}
            {shown.people.map((person) => (
                <div key={person.contactId} className="section threads campaign-tabs-inbox-person">
                    <div className="threads-head">
                        <h3>{person.name ?? person.contactId}</h3>
                        <span className="hint">replied {when(new Date(person.at), now, zone)}</span>
                    </div>
                    {shown.pluginId !== null && <PersonThreads person={person} pluginId={shown.pluginId} zone={zone} />}
                </div>
            ))}
        </div>
    );
}
