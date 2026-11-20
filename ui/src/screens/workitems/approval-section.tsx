import type { Approval, WorkItem } from '../../api/types';
import { toApprovalDetail } from '../../console/live';
import { approvalGate, readingSentence, type ApprovalGate, type Said } from '../../console/waiting-model';
import { ActionsRow } from '../../layout/actions-row';
import { href } from '../../router';
import { MessageCard } from '../attention/message-card';
import { SearchLines } from '../attention/search-lines';
import { REPLIES, STALE_SENTENCE, isStale } from '../attention/message-edit';
import { Doubts } from '../send-modes/doubts';

/** What the open item's approval is, as the shared gate holds it: read whole, or why it could not be. */
export type Read = { whole: Approval | undefined; unread: Said | undefined };

type Props = {
    item: WorkItem;
    approvalId: string;
    read: Read;
    busy: boolean;
    /** The press of Approve is checking the message against its work item. */
    checking: boolean;
    onApprove: () => void;
    onReject: () => void;
};

/** What the panel says while the press of Approve checks the message, before it approves. */
const checkingSentence = 'This message is being checked against its work item before it is approved.';

/**
 * What the panel says of an approval it could not read, before what the runtime said, or before the words that say it
 * did not answer in time: the gate's own sentence names a Details control this panel does not draw. Here a person closes
 * the item and opens it again, which reads the approval again; the failure shown is cleared when that read starts.
 */
const unreadSentence = (said: Said): string =>
    `This approval could not be read, so it cannot be approved here; close this item and open it again to read it again.${
        said.unanswered === true ? '' : ' The runtime said:'
    }`;

function Held({ gate }: { gate: ApprovalGate }) {
    if (gate.live) return null;
    if ('reading' in gate)
        return (
            <p className="notice" role="status">
                {readingSentence('needs')}
            </p>
        );
    const { code, message } = gate.unread;
    return (
        <p className="notice" role="status">
            {unreadSentence(gate.unread)} {code !== null && <code>{code}</code>} {message}
        </p>
    );
}

/**
 * The approval a waiting item waits for, decided from its panel by the rule every place that approves keeps (the
 * shared gate of `console/waiting.ts`): Approve is live only once the approval has been read whole, whatever it does;
 * a doubted send's doubt is drawn first; a message that no longer matches its work item, as the panel read the item,
 * is not offered, and at the press it is checked against its work item again. Reject needs no read, as on Needs you
 * and the chat's cards: it sends nothing to anybody.
 */
export function ApprovalSection({ item, approvalId, read, busy, checking, onApprove, onReject }: Props) {
    const { whole } = read;
    const gate = approvalGate(whole, read.unread);
    const stale = whole !== undefined && isStale(whole, item);
    const detail = whole === undefined ? null : toApprovalDetail(whole.preview);
    const intent = detail !== null && !detail.body ? detail.intent : null;
    return (
        <div className="section waiting">
            <a className="notice" href={href({ screen: 'attention', kind: 'approvals', id: approvalId })}>
                Waiting for your approval ›
            </a>
            {whole && <Doubts preview={whole.preview} />}
            <Held gate={gate} />
            {stale && (
                <p className="notice" role="status">
                    {STALE_SENTENCE}
                </p>
            )}
            {checking && (
                <p className="notice" role="status">
                    {checkingSentence}
                </p>
            )}
            {intent && (
                <div className="preview">
                    <p>{intent}</p>
                    <SearchLines search={detail?.search ?? null} />
                </div>
            )}
            {whole && detail && <MessageCard detail={detail} reply={whole.operation === REPLIES} />}
            <ActionsRow>
                <button
                    className="btn primary"
                    type="button"
                    disabled={busy || checking || !gate.live || stale}
                    onClick={onApprove}
                >
                    Approve
                </button>
                <button className="btn danger" type="button" disabled={busy || checking} onClick={onReject}>
                    Reject
                </button>
            </ActionsRow>
        </div>
    );
}
