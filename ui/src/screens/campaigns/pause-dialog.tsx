import { useState } from 'preact/hooks';
import { ApiError, Unpaired } from '../../api/client';
import { useApi } from '../../api/context';
import { session } from '../../api/session';
import type { Campaign } from '../../api/types';
import * as store from './store';
import { Dialog } from '../../layout/dialog';
import { Notice } from '../../layout/notice';

type Props = { campaign: Campaign; kind: 'pause' | 'start'; onDone: () => void; onClose: () => void };

/** The provider campaign's own name for itself: the plugin that holds its pin, as a person would write it. */
function provider(campaign: Campaign): string | null {
    const pin = campaign.external_ids.find((p) => p.kind === 'campaign');
    return pin ? pin.plugin_id.charAt(0).toUpperCase() + pin.plugin_id.slice(1) : null;
}

/** What pausing or starting does, said before it is done: Jason's own release of work, and nothing at the provider. */
function says(campaign: Campaign, kind: 'pause' | 'start'): string {
    if (kind === 'start') return "Jason releases this campaign's work again.";
    const name = provider(campaign);
    const sequence = name ? ` ${name}'s sequence keeps sending until a role pauses it there.` : '';
    return `Jason stops releasing this campaign's work. Work already running finishes.${sequence} Your reason is recorded.`;
}

/** Pause or Start, with an optional reason the journal keeps. */
export function TransitionDialog({ campaign, kind, onDone, onClose }: Props) {
    const api = useApi();
    const [reason, setReason] = useState('');
    const [busy, setBusy] = useState(false);
    const [refused, setRefused] = useState<string | null>(null);
    const verb = kind === 'pause' ? 'Pause' : 'Start';

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
                    <button class="btn primary" type="button" disabled={busy} onClick={() => void act()}>
                        {verb}
                    </button>
                </>
            }
        >
            <p>{says(campaign, kind)}</p>
            <div class="field">
                <label for="transition-reason">Reason (optional, kept in the journal)</label>
                <textarea
                    id="transition-reason"
                    class="textarea"
                    aria-label="Reason"
                    maxLength={2000}
                    value={reason}
                    onInput={(e) => setReason((e.target as HTMLTextAreaElement).value)}
                />
            </div>
            {refused && <Notice danger>{refused}</Notice>}
        </Dialog>
    );
}
