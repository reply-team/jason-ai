import { useState } from 'preact/hooks';
import type { Approval } from '../../api/types';
import { clock, relative } from '../../time/format';
import { DecideDialog } from './decide-dialog';
import { reasonLabel, title } from './labels';
import { ActionsRow } from '../../layout/actions-row';
import { PanelHeader } from '../../layout/panel-header';

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
}: Props) {
    const [rejecting, setRejecting] = useState(false);
    const preview = approval.preview as { intent?: unknown; contact?: { name?: string; value?: string } };
    const intent = typeof preview.intent === 'string' ? preview.intent : null;
    const person = preview.contact?.name ?? approval.contact_name;
    const pending = approval.status === 'pending';
    const heading = title(approval.operation, approval.contact_name);
    return (
        <div class="panel">
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
                        <button class="btn primary" type="button" disabled={busy} onClick={() => onApprove()}>
                            Approve
                        </button>
                        <button class="btn danger" type="button" disabled={busy} onClick={() => setRejecting(true)}>
                            Reject
                        </button>
                    </>
                )}
            </ActionsRow>
            <div class="panel-body">
                {intent && (
                    <div class="section">
                        <h3>What will happen</h3>
                        <div class="preview">
                            <p>{intent}</p>
                        </div>
                    </div>
                )}
                <dl class="facts">
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
                <details class="fold">
                    <summary>Raw request</summary>
                    <pre>{JSON.stringify(approval.subject, null, 2)}</pre>
                </details>
            </div>
            {pending && (
                <div class="panel-footer">
                    <span class="next">{remaining}</span>
                    <span class="spacer" />
                    <button class="btn ghost" type="button" onClick={onSkip}>
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
