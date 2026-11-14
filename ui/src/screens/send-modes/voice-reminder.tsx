import { useEffect, useState } from 'react';
import { useApi } from '../../api/context';
import type { Campaign } from '../../api/types';
import { t } from '../../platform/i18n';
import { readPreferences } from '../settings/voice-model';
import { installationAutomatic, remindsOfVoice, voiceUnset } from './model';
import { openSettings } from './open-settings';
import { hiddenNow, hide } from './reminder';

type Props = {
    /** The agent in front of the person: the approval's campaign, whose own mode may go automatically. */
    campaignId: string;
};

/**
 * A quiet line on an approval's card while messages go out without the person's look and their name or tone is not
 * set, which is what messages are written from. While every mode in force waits for the person it never shows. It reads
 * the preferences once, and the agent only when the installation's own answers do not already go automatically; it
 * points to where the two are set, and hides for seven days when the person asks, kept in the page's storage. It is a
 * courtesy: what it cannot read it does not say.
 */
export function VoiceReminder({ campaignId }: Props) {
    const api = useApi();
    const [due, setDue] = useState(false);
    const [hidden, setHidden] = useState(() => hiddenNow(new Date()));
    useEffect(() => {
        if (hidden) return;
        let current = true;
        (async () => {
            const items = await readPreferences(api);
            // The agent is read only when it can change the answer.
            if (!voiceUnset(items)) return false;
            if (installationAutomatic(items)) return true;
            const agent = await api.call<Campaign>('campaign.get', { campaign_id: campaignId });
            return remindsOfVoice(items, agent.send_modes_in_force);
        })().then(
            (shows) => current && setDue(shows),
            () => {},
        );
        return () => {
            current = false;
        };
    }, [api, hidden, campaignId]);
    if (hidden || !due) return null;
    return (
        <p className="hint voice-reminder">
            {t('modes.reminder')}{' '}
            <button className="btn ghost" type="button" onClick={openSettings}>
                {t('modes.settings.open')}
            </button>{' '}
            <button
                className="btn ghost"
                type="button"
                onClick={() => {
                    hide(new Date());
                    setHidden(true);
                }}
            >
                {t('modes.reminder.hide')}
            </button>
        </p>
    );
}
