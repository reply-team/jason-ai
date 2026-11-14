import { t } from '../../platform/i18n';

const verbs: Record<string, string> = {
    'campaign.enroll': 'Enroll',
    'campaign.activate': 'Activate',
    'campaign.pause': 'Pause',
    'campaign.get': 'Read',
    'enrollment.stop': 'Stop',
    'enrollment.list': 'List enrollments of',
    'list_membership.add': 'Add to a list',
    'optout.record': 'Record an opt-out for',
    'conversation.list': 'Read conversations of',
    'conversation.get': 'Read a conversation of',
    'engagement.summarize': 'Summarize engagement of',
};

export function operationLabel(operation: string): string {
    return verbs[operation] ?? operation;
}

export function title(operation: string, contactName: string | null): string {
    return contactName ? `${operationLabel(operation)} ${contactName}` : operationLabel(operation);
}

/** The row's mark for an approval's status: waiting, decided one way or the other, or ended without a decision. */
export function approvalMark(status: string): { kind: string; glyph: string } {
    switch (status) {
        case 'pending':
            return { kind: 'wait', glyph: '‖' };
        case 'approved':
            return { kind: 'ok', glyph: '✓' };
        case 'rejected':
            return { kind: 'fail', glyph: '✕' };
        default:
            return { kind: 'off', glyph: '–' };
    }
}

/** What the row says beside the time: who decided, or that nobody did (superseded by a changed input, cancelled with its work item). */
export function approvalNote(status: string, decidedBy: string | null | undefined): string | null {
    if (status === 'superseded' || status === 'cancelled') return status;
    return decidedBy ? `decided by ${decidedBy}` : null;
}

/** The reasons the send mode gives on an approval: why a send waits for the person, or why one went without asking. */
const sendModeReasons = [
    'sent_from_chat',
    'doubted',
    'awaiting_judgement',
    'awaiting_nature',
    'answers_no_verdict',
    'reply_not_from_a_person',
    'escalated',
    'edited_after_review',
    'not_reviewed',
    'daily_ceiling',
    'automatic_mode',
] as const;

/** Why an approval is here, in a sentence; a reason this build does not know is shown as the runtime sent it. */
export function reasonLabel(reason: string): string {
    switch (reason) {
        case 'approval_required':
            return 'Approval is required for every operation of this kind for this agent.';
        case 'input_changed':
            return 'The input changed since an earlier approval, so it is asked again.';
        default: {
            const known = sendModeReasons.find((each) => each === reason);
            return known === undefined ? reason : t(`modes.park.${known}`);
        }
    }
}
