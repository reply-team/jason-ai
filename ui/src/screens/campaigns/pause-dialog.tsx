import { useState } from 'react';
import { ApiError, Unpaired } from '../../api/client';
import { useApi } from '../../api/context';
import { session } from '../../api/session';
import type { Campaign } from '../../api/types';
import * as store from './store';
import { Dialog } from '../../layout/dialog';
import { Notice } from '../../layout/notice';

/** Pause stops an active campaign's work; Resume releases a paused one's again; Start releases a draft's for the first time. */
export type Transition = 'pause' | 'resume' | 'start';

type Props = { campaign: Campaign; kind: Transition; onDone: () => void; onClose: () => void };

/** Each move by the word its button and its dialog say. */
export const verbs: Record<Transition, string> = { pause: 'Pause', resume: 'Resume', start: 'Start' };

/**
 * What the runtime lets this campaign's status move to: an active one pauses, a paused one resumes and a draft starts,
 * each through `campaign.pause` or `campaign.start`; an archived campaign is over and moves nowhere.
 */
export function transitions(campaign: Campaign): Transition[] {
    if (campaign.status === 'active') return ['pause'];
    if (campaign.status === 'paused') return ['resume'];
    if (campaign.status === 'draft') return ['start'];
    return [];
}

/** The provider campaign's own name for itself: the plugin that holds its pin, as a person would write it. */
function provider(campaign: Campaign): string | null {
    const pin = campaign.external_ids.find((p) => p.kind === 'campaign');
    return pin ? pin.plugin_id.charAt(0).toUpperCase() + pin.plugin_id.slice(1) : null;
}

/** What pausing or starting does, said before it is done: Jason's own release of work, and nothing at the provider. */
function says(campaign: Campaign, kind: Transition): string {
    if (kind === 'resume') return "Jason releases this agent's work again.";
    if (kind === 'start') return "Jason starts releasing this agent's work.";
    const name = provider(campaign);
    const sequence = name ? ` ${name}'s sequence keeps sending until a role pauses it there.` : '';
    return `Jason stops releasing this agent's work. Work already running finishes.${sequence} Your reason is recorded.`;
}

/** Pause, Resume or Start, with an optional reason the journal keeps. */
export function TransitionDialog({ campaign, kind, onDone, onClose }: Props) {
    const api = useApi();
    const [reason, setReason] = useState('');
    const [busy, setBusy] = useState(false);
    const [refused, setRefused] = useState<string | null>(null);
    const verb = verbs[kind];

    const act = async () => {
        setBusy(true);
        setRefused(null);
        try {
            await store.transition(
                api,
                kind === 'pause' ? 'campaign.pause' : 'campaign.start',
                campaign.id,
                reason.trim() || undefined,
            );
            onDone();
        } catch (error) {
            if (error instanceof Unpaired) session.clear();
            else setRefused(error instanceof ApiError ? error.message : String(error));
        } finally {
            setBusy(false);
        }
    };

    return (
        <Dialog
            title={
                <>
                    {verb} {campaign.name}
                </>
            }
            label={`${verb} ${campaign.name}`}
            onClose={onClose}
            buttons={
                <>
                    <button className="btn primary" type="button" disabled={busy} onClick={() => void act()}>
                        {verb}
                    </button>
                </>
            }
        >
            <p>{says(campaign, kind)}</p>
            <div className="field">
                <label htmlFor="transition-reason">Reason (optional, kept in the journal)</label>
                <textarea
                    id="transition-reason"
                    className="textarea"
                    aria-label="Reason"
                    maxLength={2000}
                    value={reason}
                    onChange={(e) => setReason((e.target as HTMLTextAreaElement).value)}
                />
            </div>
            {refused && <Notice danger>{refused}</Notice>}
        </Dialog>
    );
}
