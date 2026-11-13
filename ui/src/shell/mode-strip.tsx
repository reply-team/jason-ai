import { t } from '../platform/i18n';
import { mode, modeRefusal, switchMode, type Mode } from '../platform/mode';

const modes: readonly Mode[] = ['demo', 'live'];

/**
 * The strip above every screen in Demo: that the data is sample, that nothing is real or sent, that the demo starts
 * fresh each time; and the two modes, the one in force marked, the other switched to. A short form stands in for the
 * long one in a narrow window (each shell's stylesheet). In Live there is none.
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
