import { useEffect, useState } from 'react';
import { useApi } from '../../api/context';
import { t } from '../../platform/i18n';
import { readPreferences, senderKey, toneKey, valueOf } from '../settings/voice-model';
import { openSettings } from './open-settings';
import { hiddenNow, hide } from './reminder';

/**
 * A quiet line on an approval's card once the modes are answered: the person's name and tone are not set, which is
 * what messages are written from. It reads the two preferences once, points to where they are set, and hides for seven
 * days when the person asks, kept in the page's storage. It is a courtesy: what it cannot read it does not say.
 */
export function VoiceReminder() {
    const api = useApi();
    const [unset, setUnset] = useState(false);
    const [hidden, setHidden] = useState(() => hiddenNow(new Date()));
    useEffect(() => {
        if (hidden) return;
        let current = true;
        readPreferences(api).then(
            (items) => current && setUnset(valueOf(items, senderKey) === null || valueOf(items, toneKey) === null),
            () => {},
        );
        return () => {
            current = false;
        };
    }, [api, hidden]);
    if (hidden || !unset) return null;
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
