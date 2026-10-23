import { useState } from 'preact/hooks';
import { ApiError, Unpaired } from '../../api/client';
import { useApi } from '../../api/context';
import { session } from '../../api/session';
import type { Contact } from '../../api/types';
import { CampaignPicker } from '../../pickers/campaign-picker';
import * as store from './store';
import { Dialog } from '../../layout/dialog';
import { Notice } from '../../layout/notice';

type Props = { person: Contact; onClose: () => void };

/**
 * Adding the open person to a campaign: which one and an optional reason the journal keeps. An add closes the dialog,
 * the card's new badge being what it did; a campaign that already has them, or refuses them, is said here instead.
 */
export function AddDialog({ person, onClose }: Props) {
    const api = useApi();
    const [campaign, setCampaign] = useState<{ id: string; name: string } | null>(null);
    const [reason, setReason] = useState('');
    const [busy, setBusy] = useState(false);
    const [refused, setRefused] = useState<string | null>(null);
    const [said, setSaid] = useState<string | null>(null);
    const excluded =
        campaign !== null &&
        (person.campaigns ?? []).some((c) => c.campaign_id === campaign.id && c.state === 'excluded');

    const add = async () => {
        if (!campaign) return;
        setBusy(true);
        setRefused(null);
        setSaid(null);
        try {
            const result = await store.add(api, person.id, campaign.id, reason.trim() || undefined);
            const item = result.items[0];
            if (item?.status === 'rejected') setRefused(item.error?.message ?? 'The campaign refused this person.');
            else if (item?.status === 'already_member') setSaid(`${campaign.name} already has them.`);
            else onClose();
        } catch (error) {
            if (error instanceof Unpaired) session.clear();
            else setRefused(error instanceof ApiError ? error.message : String(error));
        } finally {
            setBusy(false);
        }
    };

    return (
        <Dialog
            title={'Add to campaign'}
            label="Add to campaign"
            onClose={onClose}
            buttons={
                <>
                    <button class="btn primary" type="button" disabled={!campaign || busy} onClick={() => void add()}>
                        Add
                    </button>
                </>
            }
        >
            <div class="field">
                <CampaignPicker
                    label={campaign?.name ?? 'Choose a campaign'}
                    none="Choose a campaign"
                    onPick={setCampaign}
                />
                {excluded && <div class="hint">They were excluded from this campaign; adding re-enrols them.</div>}
            </div>
            <div class="field">
                <label for="add-reason">Reason (optional, kept in the journal)</label>
                <textarea
                    id="add-reason"
                    class="textarea"
                    aria-label="Reason"
                    maxLength={2000}
                    value={reason}
                    onInput={(e) => setReason((e.target as HTMLTextAreaElement).value)}
                />
            </div>
            {said && (
                <p class="hint" role="status">
                    {said}
                </p>
            )}
            {refused && <Notice danger>{refused}</Notice>}
        </Dialog>
    );
}
