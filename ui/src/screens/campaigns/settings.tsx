import type { Campaign } from '../../api/types';
import { Details } from './details';

type Props = { campaign: Campaign; now: Date; zone: string };

/** The campaign's record as the runtime keeps it; the moves its status allows are in the campaign's header. */
export function CampaignSettings({ campaign, now, zone }: Props) {
    return <Details campaign={campaign} now={now} zone={zone} />;
}
