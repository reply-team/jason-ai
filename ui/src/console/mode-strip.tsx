import { t } from '../platform/i18n';
import { mode, modeRefusal, modeUnopened, switchMode, type Mode } from '../platform/mode';

const modes: readonly Mode[] = ['demo', 'live'];

/**
 * The bar above the console, over the sidebar and every panel, in both modes. On the left a dot and a mark: the word
 * Live, or in the demo one short sentence saying the data is sample; in a narrow window the demo's sentence is
 * shorter (`console.css`). The two modes differ by colour from the existing tokens. A demo that could not be opened
 * leaves the person in Live, and the desktop's sentence about it stands in the bar until closed; a refused switch
 * is said there too. On the right, in this order: `data-slot="open-in"`, the one place a control for opening the
 * work elsewhere goes, then the Demo | Live switch, the mode in force marked, asking the desktop through the seam.
 */
export function ModeStrip() {
    const unopened = modeUnopened.value;
    const refusal = modeRefusal.value;
    const demo = mode.value === 'demo';
    return (
        <div className={`mode-strip ${demo ? 'mode-demo' : 'mode-live'}`} role="region" aria-label={t('mode.strip')}>
            <span className="mode-dot" aria-hidden="true" />
            {demo ? (
                <>
                    <span className="mode-mark mode-strip-long">{t('mode.strip.long')}</span>
                    <span className="mode-strip-short">{t('mode.strip.short')}</span>
                </>
            ) : (
                <span className="mode-mark">{t('mode.switch.live')}</span>
            )}
            {!demo && unopened !== null && (
                <span className="mode-unopened" role="status" aria-label={t('mode.unopened')}>
                    <span>{unopened}</span>
                    <button type="button" className="mode-unopened-close" onClick={() => (modeUnopened.value = null)}>
                        {t('mode.unopened.close')}
                    </button>
                </span>
            )}
            {demo && refusal !== null && (
                <span className="mode-strip-refusal" role="alert">
                    {refusal}
                </span>
            )}
            <span className="mode-spacer" />
            <div className="mode-open-in" data-slot="open-in" />
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
