import { t } from '../../platform/i18n';
import { sourceWords } from './agent-modes';
import { kinds, modeWords, sendModeOf } from './model';
import { openSettings } from './open-settings';

/**
 * On the card of a send the send mode let go (`automatic_mode`): that nobody was asked, under which kind's mode and from
 * where, that a review passed its text first, and where the mode is changed: Settings for every agent, the agent's own
 * Settings tab where the agent set its own. It reads the facts the claim wrote on the preview and nothing else; a preview
 * without them draws the heading alone.
 */
export function ReleasedBySendMode({ preview }: { preview: Record<string, unknown> }) {
    const facts = sendModeOf(preview);
    const kind = kinds.find((each) => each === facts?.temperature);
    return (
        <div className="section send-modes-released">
            <h3>{t('modes.released.heading')}</h3>
            {facts && kind && facts.mode && (
                <p>
                    {t('modes.released.mode', {
                        kind: t(`modes.kind.${kind}`),
                        mode: modeWords(facts.mode).name,
                        source: sourceWords(facts.source ?? ''),
                    })}
                </p>
            )}
            {facts?.review && <p className="muted">{t('modes.released.review')}</p>}
            {facts?.source === 'campaign' ? (
                <p className="hint">{t('modes.released.change.campaign')}</p>
            ) : (
                <p className="hint">
                    {t('modes.released.change')}{' '}
                    <button className="btn ghost" type="button" onClick={openSettings}>
                        {t('modes.settings.open')}
                    </button>
                </p>
            )}
        </div>
    );
}
