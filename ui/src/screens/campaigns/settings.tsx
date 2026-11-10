import type { Campaign } from '../../api/types';
import { Details } from './details';
import type { Transition } from './pause-dialog';

type Props = { campaign: Campaign; now: Date; zone: string; onAct: (act: Transition) => void };

/**
 * What the runtime lets this campaign's status move to: an active one pauses, a paused one resumes and a draft starts,
 * each through `campaign.pause` or `campaign.start`; an archived campaign is over and moves nowhere.
 */
function transitions(campaign: Campaign): Transition[] {
    if (campaign.status === 'active') return ['pause'];
    if (campaign.status === 'paused') return ['resume'];
    if (campaign.status === 'draft') return ['start'];
    return [];
}

const labels: Record<Transition, string> = { pause: 'Pause', resume: 'Resume', start: 'Start' };

/** The campaign's record as the runtime keeps it, with the moves its status allows. */
export function CampaignSettings({ campaign, now, zone, onAct }: Props) {
    const moves = transitions(campaign);
    return (
        <>
            {moves.length > 0 && (
                <div className="campaign-tabs-settings">
                    {moves.map((move) => (
                        <button key={move} className="btn" type="button" onClick={() => onAct(move)}>
                            {labels[move]}
                        </button>
                    ))}
                </div>
            )}
            <Details campaign={campaign} now={now} zone={zone} />
        </>
    );
}
