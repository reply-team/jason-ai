import type { Campaign } from '../../api/types';
import { consoleShell } from '../../platform/console-shell';
import { ArchiveSection, type WasOpen } from './archive';
import { Details } from './details';

type Props = { campaign: Campaign; now: Date; zone: string; onArchived: (archived: Campaign, open: WasOpen) => void };

/**
 * The campaign's record as the runtime keeps it; the moves its status allows are in the campaign's header. In the
 * console its last part is where the person archives the agent; the classic shell draws the record alone.
 */
export function CampaignSettings({ campaign, now, zone, onArchived }: Props) {
    return (
        <Details
            campaign={campaign}
            now={now}
            zone={zone}
            last={consoleShell.loaded ? <ArchiveSection campaign={campaign} onArchived={onArchived} /> : null}
        />
    );
}
