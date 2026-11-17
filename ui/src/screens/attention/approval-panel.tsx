import { useEffect, useMemo, useState } from 'react';
import { useApi } from '../../api/context';
import type { Approval, WorkItem } from '../../api/types';
import { clock, relative } from '../../time/format';
import { DecideDialog } from './decide-dialog';
import { reasonLabel, title } from './labels';
import { ActionsRow } from '../../layout/actions-row';
import { CopyPromptButton } from './copy-prompt-button';
import { SessionLink } from './session-link';
import { PanelHeader } from '../../layout/panel-header';
import { toApprovalDetail } from '../../console/live';
import { MessageCard } from './message-card';
import { MessageEditor } from './message-editor';
import { SenderLine } from './sender-line';
import { senderReads } from '../../console/senders';
import { Doubts } from '../send-modes/doubts';
import { SendModesQuestion } from '../send-modes/question';
import { ReleasedBySendMode } from '../send-modes/released';
import { VoiceReminder } from '../send-modes/voice-reminder';
import {
    REPLIES,
    STALE_SENTENCE,
    awaitNewApproval,
    checkMessage,
    editable,
    isMessage,
    isStale,
    uncheckedSentence,
} from './message-edit';
import { asking, noteAsked, noteEdit } from './reasking';
import { ASKING_SENTENCE, withWords } from './reasking-model';

type Props = {
    approval: Approval;
    now: Date;
    zone: string;
    busy: boolean;
    remaining: string;
    /** What the button that leaves this one says: the next waiting thing on Attention, the campaign elsewhere. */
    skipLabel?: string;
    onApprove: () => void;
    onReject: (reason?: string) => void;
    onSkip: () => void;
    onClose: () => void;
    /** Called with the new approval once the runtime has asked again under an edited text. */
    onReasked?: (approvalId: string) => void;
    /** Called once a saved edit has withdrawn this approval, so a list showing it can be read again and keep its row. */
    onSuperseded?: () => void;
    /** How the card waits for the new approval; the default is a poll a second apart for half a minute. */
    reaskPoll?: { tries?: number; delayMs?: number } | undefined;
};

export function ApprovalPanel({
    approval,
    now,
    zone,
    busy,
    remaining,
    skipLabel = 'Skip to next',
    onApprove,
    onReject,
    onSkip,
    onClose,
    onReasked,
    onSuperseded,
    reaskPoll,
}: Props) {
    const api = useApi();
    // The accounts a pending send's sender line reads, one read per approval shown (the line takes its mark when an approval is shown).
    const senders = useMemo(() => senderReads(), []);
    const [rejecting, setRejecting] = useState(false);
    // A message approval is checked against its work item before Approve is offered, and again at the press.
    const watched = approval.status === 'pending' && isMessage(approval.operation);
    const [item, setItem] = useState<WorkItem | null>(null);
    const [unread, setUnread] = useState<string | null>(null);
    // A message edited on this page: its approval was withdrawn and the runtime asks again with the saved words. The
    // panel stays, with those words and no act, until the new approval comes (`reasking.ts`).
    const edit = asking(approval.id);
    const waits = edit !== undefined;
    useEffect(() => {
        setItem(null);
        setUnread(null);
        if (!watched || waits) return;
        let current = true;
        void checkMessage(api, approval).then((checked) => {
            if (!current || checked === null) return;
            if ('unchecked' in checked) setUnread(checked.unchecked);
            else setItem(checked.item);
        });
        return () => {
            current = false;
        };
    }, [api, watched, waits, approval.id, approval.work_item_id]);
    const stale = watched && item !== null && isStale(approval, item);
    // While the runtime has not asked again, the panel waits for the new approval and hands it on when it arrives.
    useEffect(() => {
        if (!waits) return;
        let current = true;
        awaitNewApproval(api, approval.work_item_id, approval.id, { ...reaskPoll, stopped: () => !current }).then(
            (id) => {
                if (!current || !id) return;
                noteAsked(approval.work_item_id, id);
                onReasked?.(id);
            },
            () => {},
        );
        return () => {
            current = false;
        };
    }, [api, waits, approval.id, approval.work_item_id]);

    async function approveChecked() {
        if (!watched) return onApprove();
        const checked = await checkMessage(api, approval);
        if (checked === null) return;
        if ('unchecked' in checked) return setUnread(checked.unchecked);
        setItem(checked.item);
        if (!checked.stale) onApprove();
    }
    const preview = approval.preview as { intent?: unknown; contact?: { name?: string; value?: string } };
    const intent = typeof preview.intent === 'string' ? preview.intent : null;
    const person = preview.contact?.name ?? approval.contact_name;
    // An edited message shows the words saved, which the runtime asks again with, never the withdrawn ones.
    const read = toApprovalDetail(approval.preview);
    const detail = edit === undefined ? read : withWords(read, edit.words);
    const { from, channel } = detail;
    // No act is offered on an approval an edit withdrew: Approve and Reject come back with the approval asked again.
    const pending = approval.status === 'pending' && edit === undefined;
    const heading = title(approval.operation, approval.contact_name);
    return (
        <div className="panel">
            <PanelHeader
                title={heading}
                sub={
                    <>
                        in {approval.campaign_name} · requested {relative(new Date(approval.requested_at), now)} by the{' '}
                        {approval.plugin_id} plugin
                    </>
                }
                onClose={onClose}
            />
            <ActionsRow>
                {pending && (
                    <>
                        <button
                            className="btn primary"
                            type="button"
                            disabled={busy || (watched && (item === null || stale))}
                            onClick={() => void approveChecked()}
                        >
                            Approve
                        </button>
                        <button className="btn danger" type="button" disabled={busy} onClick={() => setRejecting(true)}>
                            Reject
                        </button>
                        <CopyPromptButton
                            kind="approval"
                            itemId={approval.id}
                            campaignId={approval.campaign_id}
                            workItemId={approval.work_item_id}
                        />
                        <SessionLink kind="approval" itemId={approval.id} />
                    </>
                )}
            </ActionsRow>
            <div className="panel-body">
                {edit !== undefined && (
                    <p className="notice reasking" role="status">
                        {ASKING_SENTENCE}
                    </p>
                )}
                {stale && (
                    <p className="notice" role="status">
                        {STALE_SENTENCE}
                    </p>
                )}
                {unread && (
                    <p className="notice" role="alert">
                        {uncheckedSentence(unread)}
                    </p>
                )}
                {pending && <Doubts preview={approval.preview} />}
                {approval.reason === 'automatic_mode' && <ReleasedBySendMode preview={approval.preview} />}
                {intent && (
                    <div className="section">
                        <h3>What will happen</h3>
                        <div className="preview">
                            <p>{intent}</p>
                        </div>
                    </div>
                )}
                <dl className="facts">
                    <dt>Why it's here</dt>
                    <dd>{reasonLabel(approval.reason)}</dd>
                    {person && (
                        <>
                            <dt>Person</dt>
                            <dd>
                                {person}
                                {preview.contact?.value ? ` · ${preview.contact.value}` : ''}
                            </dd>
                        </>
                    )}
                    {from && (
                        <>
                            <dt>From</dt>
                            <dd>
                                {from}
                                {pending && <SenderLine approval={approval} reads={senders} />}
                            </dd>
                        </>
                    )}
                    {channel && (
                        <>
                            <dt>Channel</dt>
                            <dd>{channel}</dd>
                        </>
                    )}
                    <dt>Provider</dt>
                    <dd>{approval.plugin_id}</dd>
                    <dt>Requested</dt>
                    <dd>{clock(new Date(approval.requested_at), zone)}</dd>
                    {approval.decided_by?.id && (
                        <>
                            <dt>Decided</dt>
                            <dd>
                                {approval.status} by {approval.decided_by.id}
                                {approval.decision_reason ? ` · ${approval.decision_reason}` : ''}
                            </dd>
                        </>
                    )}
                </dl>
                <MessageCard detail={detail} reply={approval.operation === REPLIES} />
                {pending && watched && item && editable(approval) && (
                    <MessageEditor
                        approval={approval}
                        item={item}
                        busy={busy}
                        onMoved={setItem}
                        onSaved={(saved) => {
                            setItem(saved);
                            noteEdit(approval, saved);
                            onSuperseded?.();
                        }}
                    />
                )}
                {pending && <SendModesQuestion unset={approval.send_modes_unset} approvalId={approval.id} />}
                {pending && <VoiceReminder campaignId={approval.campaign_id} />}
                <details className="fold">
                    <summary>Raw request</summary>
                    <pre>{JSON.stringify(approval.subject, null, 2)}</pre>
                </details>
            </div>
            {(pending || waits) && (
                <div className="panel-footer">
                    <span className="next">{remaining}</span>
                    <span className="spacer" />
                    <button className="btn ghost" type="button" onClick={onSkip}>
                        {skipLabel}
                    </button>
                </div>
            )}
            {rejecting && (
                <DecideDialog
                    verb="Reject"
                    what={heading}
                    onCancel={() => setRejecting(false)}
                    onConfirm={(reason) => {
                        setRejecting(false);
                        onReject(reason);
                    }}
                />
            )}
        </div>
    );
}
