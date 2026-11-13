import { channelWords, type ApprovalDetail } from '../../console/live';

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

/**
 * The message an approval would send, as it was written: an email's sender, recipient, subject and body in that order,
 * a LinkedIn message's text, a template with the example the runtime made of it. Everything is text, never markup. It
 * claims nothing about how the message reaches the person, and the sentence under it says what it leaves out.
 */
export function MessageCard({ detail, reply = false }: { detail: ApprovalDetail; reply?: boolean }) {
    if (!detail.body) return null;
    const email = !reply && detail.channel === channelWords('email');
    const template = detail.compose === 'template';
    return (
        <div className="section message-card">
            <h3>The message as written</h3>
            <dl className="facts message-facts">
                {email && detail.from && <Row label="From">{detail.from}</Row>}
                {(email || reply) && detail.to && <Row label="To">{detail.to}</Row>}
                {reply && detail.channel && <Row label="Channel">{detail.channel}</Row>}
                {detail.subject && (
                    <Row label="Subject">
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
                Not shown: the sender's signature kept at the provider, and the provider's tracking, which are added
                when it sends.
            </p>
        </div>
    );
}
