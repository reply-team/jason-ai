import { t } from '../platform/i18n';
import { mode, modeRefusal, switchMode, type Mode } from '../platform/mode';

const modes: readonly Mode[] = ['demo', 'live'];

/**
 * The strip above the console in Demo, over the sidebar and every panel: that the data is sample, that nothing is real
 * and nothing is sent to anyone; and the two modes, the one in force marked, Live the way back to the person's own
 * installation. A short form stands in for the long one in a narrow window (`console.css`). In Live there is none.
 */
export function ModeStrip() {
    if (mode.value !== 'demo') return null;
    const refusal = modeRefusal.value;
    return (
        <div className="mode-strip" role="region" aria-label={t('mode.strip')}>
            <span className="mode-strip-long">{t('mode.strip.long')}</span>
            <span className="mode-strip-short">{t('mode.strip.short')}</span>
            {refusal !== null && (
                <span className="mode-strip-refusal" role="alert">
                    {refusal}
                </span>
            )}
            <div className="mode-switch" role="group" aria-label={t('mode.switch')}>
                {modes.map((each) => (
                    <button
                        key={each}
                        type="button"
                        className={each === mode.value ? 'on' : undefined}
                        aria-pressed={each === mode.value}
                        onClick={() => void switchMode(each)}
                    >
                        {t(each === 'demo' ? 'mode.switch.demo' : 'mode.switch.live')}
                    </button>
                ))}
            </div>
        </div>
    );
}
