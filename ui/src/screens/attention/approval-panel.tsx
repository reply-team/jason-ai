import { useEffect, useState } from 'react';
import { Unpaired } from '../../api/client';
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
import { Doubts } from '../send-modes/doubts';
import { SendModesQuestion } from '../send-modes/question';
import { ReleasedBySendMode } from '../send-modes/released';
import { VoiceReminder } from '../send-modes/voice-reminder';
import { REPLIES, awaitNewApproval, editable, isMessage, isStale, readItem, sentence } from './message-edit';

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
    /** Called once a saved edit has withdrawn this approval, so a list showing it can be read again. */
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
    const [rejecting, setRejecting] = useState(false);
    // A message approval is checked against its work item before Approve is offered, and again at the press.
    const watched = approval.status === 'pending' && isMessage(approval.operation);
    const [item, setItem] = useState<WorkItem | null>(null);
    const [unread, setUnread] = useState<string | null>(null);
    const [savedHere, setSavedHere] = useState(false);
    useEffect(() => {
        setItem(null);
        setUnread(null);
        setSavedHere(false);
        if (!watched) return;
        let current = true;
        readItem(api, approval.work_item_id).then(
            (read) => current && setItem(read),
            (error: unknown) => current && !(error instanceof Unpaired) && setUnread(sentence(error)),
        );
        return () => {
            current = false;
        };
    }, [api, watched, approval.id, approval.work_item_id]);
    const stale = watched && item !== null && isStale(approval, item);
    // After a save the runtime asks again; the card waits for the new approval and hands it on when it arrives.
    useEffect(() => {
        if (!savedHere) return;
        let current = true;
        awaitNewApproval(api, approval.work_item_id, approval.id, { ...reaskPoll, stopped: () => !current }).then(
            (id) => current && id && onReasked?.(id),
            () => {},
        );
        return () => {
            current = false;
        };
    }, [api, savedHere, approval.id, approval.work_item_id]);

    async function approveChecked() {
        if (!watched) return onApprove();
        try {
            const fresh = await readItem(api, approval.work_item_id);
            setItem(fresh);
            if (!isStale(approval, fresh)) onApprove();
        } catch (error) {
            if (!(error instanceof Unpaired)) setUnread(sentence(error));
        }
    }
    const preview = approval.preview as { intent?: unknown; contact?: { name?: string; value?: string } };
    const intent = typeof preview.intent === 'string' ? preview.intent : null;
    const person = preview.contact?.name ?? approval.contact_name;
    const detail = toApprovalDetail(approval.preview);
    const { from, channel } = detail;
    const pending = approval.status === 'pending';
    const heading = title(approval.operation, approval.contact_name);
    // A saved edit withdrew this approval: the card is gone, and the sentence says what happens next. The poll above
    // shows the new approval when it comes; if it never does, the sentence stays true and the screen's refresh shows it.
    if (savedHere)
        return (
            <div className="panel">
                <PanelHeader title={heading} sub={<>in {approval.campaign_name}</>} onClose={onClose} />
                <div className="panel-body">
                    <p className="notice" role="status">
                        Jason asks again with the new text when it next takes this work.
                    </p>
                </div>
            </div>
        );
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
                {stale && (
                    <p className="notice" role="status">
                        The text of this message changed after this approval was asked, so Jason will ask again with the
                        new text.
                    </p>
                )}
                {unread && (
                    <p className="notice" role="alert">
                        The message could not be checked against its work item, so it cannot be approved now: {unread}
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
                            <dd>{from}</dd>
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
                {watched && item && editable(approval) && (
                    <MessageEditor
                        approval={approval}
                        item={item}
                        busy={busy}
                        onMoved={setItem}
                        onSaved={(saved) => {
                            setItem(saved);
                            setSavedHere(true);
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
            {pending && (
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
