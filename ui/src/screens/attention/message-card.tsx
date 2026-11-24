import { useEffect, useState } from 'react';
import { useApi } from '../../api/context';
import type { WorkItem } from '../../api/types';
import { channelWords, type ApprovalDetail } from '../../console/live';
import { followsSentence, type Followed, type FollowsWords } from '../../console/follows';
import { when } from '../../time/format';

/** A person's text as a text node: the framework escapes it, and the line breaks it carries are drawn by the style. */
function Text({ value }: { value: string }) {
    return <div className="message-text">{value}</div>;
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
    return (
        <>
            <dt>{label}</dt>
            <dd>{children}</dd>
        </>
    );
}

/** The Follows row of a letter that goes under an earlier one: that letter is read for its subject and its moment. */
function Following({ follows }: { follows: FollowsWords }) {
    const api = useApi();
    const [followed, setFollowed] = useState<Followed | null>(null);
    useEffect(() => {
        let live = true;
        api.call<WorkItem>('workitem.get', { work_item_id: follows.workItem! }).then(
            (item) => {
                if (!live) return;
                // A work item's context.input is the operation's arguments themselves.
                const input = (item.context as { input?: { message?: { subject?: unknown }; send_after?: unknown } })
                    .input;
                const subject = input?.message?.subject;
                const sendAfter = input?.send_after;
                // The letter's own moment: its send_after where it has one, else when the provider accepted it.
                const at = typeof sendAfter === 'string' && sendAfter ? sendAfter : item.finished_at;
                setFollowed({
                    subject: typeof subject === 'string' && subject ? subject : null,
                    moment: at
                        ? when(new Date(at), new Date(), Intl.DateTimeFormat().resolvedOptions().timeZone)
                        : null,
                });
            },
            () => undefined,
        );
        return () => {
            live = false;
        };
    }, [api, follows.workItem]);
    return <>{followsSentence(follows, followed)}</>;
}

/**
 * The message an approval would send, as it was written: an email's sender, recipient, subject and body in that order,
 * a LinkedIn message's text, a reply's account, recipient, channel and text, a template with the example the runtime
 * made of it. Everything is text, never markup. It claims nothing about how the message reaches the person, and the
 * sentence under it says what it leaves out.
 */
export function MessageCard({ detail, reply = false }: { detail: ApprovalDetail; reply?: boolean }) {
    if (!detail.body) return null;
    const email = !reply && detail.channel === channelWords('email');
    const template = detail.compose === 'template';
    return (
        <div className="section message-card">
            <h3>The message as written</h3>
            <dl className="facts message-facts">
                {(email || reply) && detail.from && <Row label="From">{detail.from}</Row>}
                {(email || reply) && detail.to && <Row label="To">{detail.to}</Row>}
                {reply && detail.channel && <Row label="Channel">{detail.channel}</Row>}
                {email && detail.follows && (
                    <Row label="Follows">
                        {detail.follows.workItem === null ? (
                            followsSentence(detail.follows, null)
                        ) : (
                            <Following follows={detail.follows} />
                        )}
                    </Row>
                )}
                {detail.subject && (
                    <Row label={email && detail.follows?.workItem ? 'Subject (not sent)' : 'Subject'}>
                        <Text value={detail.subject} />
                    </Row>
                )}
                <Row label={template ? 'Template' : email ? 'Body' : 'Text'}>
                    <Text value={detail.body} />
                </Row>
            </dl>
            {template && detail.example && (
                <>
                    <h4>An example the provider fills in</h4>
                    <p className="hint">
                        This is the runtime's own rendering for this person. The provider fills the template in when it
                        sends and may render it otherwise.
                    </p>
                    <dl className="facts message-facts">
                        {detail.example.subject && (
                            <Row label="Subject">
                                <Text value={detail.example.subject} />
                            </Row>
                        )}
                        {detail.example.body && (
                            <Row label="Body">
                                <Text value={detail.example.body} />
                            </Row>
                        )}
                        {detail.example.unfilled.length > 0 && (
                            <Row label="Not filled">{detail.example.unfilled.join(', ')}</Row>
                        )}
                    </dl>
                </>
            )}
            <p className="hint">
                Not shown: the sender's signature kept at the provider, the provider's tracking and, under a reply, the
                quotation of the earlier email the mailbox appends, which are added when it sends.
            </p>
        </div>
    );
}
