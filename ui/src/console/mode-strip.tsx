import type { ReactNode } from 'react';
import { t } from '../platform/i18n';
import { showDemo } from '../platform/demo-frame';
import { can, cannotHere, placeKind } from '../platform/places';
import { mode, modePreparing, modeRefusal, modeUnopened, switchMode, type Mode } from '../platform/mode';

const modes: readonly Mode[] = ['demo', 'live'];

/**
 * The strip at the head of the column right of the sidebar, over every panel, in both modes: 38 pixels high, and the
 * part of the window it is dragged by, its controls excepted (`console.css`). In Live it is white and says nothing on
 * the left, the pressed segment of the switch being the mark; in the demo a dot and one short sentence say the data is
 * sample, on a warm ground, and in a narrow window the sentence is shorter. A demo that could not be opened
 * leaves the person in Live, and the desktop's sentence about it stands in the bar until closed; a refused switch
 * is said there too. On the right, in this order: `data-slot="open-in"`, the one place a control for opening the
 * work elsewhere goes, then the Demo | Live switch, one segmented control with the mode in force pressed, asking the
 * desktop through the seam. In a browser tab the switch asks nothing of anyone: Demo shows the demo page in this tab
 * alone (`#/demo`), and the desktop, its mode and the person's installation stay as they are.
 * While the demo's sample is still being made a quiet mark says so beside the sentence, and the switch is off, its
 * reason on each button, until the desktop says the sample is ready.
 */
export function ModeStrip({
    openIn,
    nav,
}: {
    openIn?: ReactNode;
    /** The sidebar's own button, drawn where the sidebar is off the screen: whether it is shown, and the toggle. */
    nav?: { open: boolean; toggle: () => void };
}) {
    const unopened = modeUnopened.value;
    const refusal = modeRefusal.value;
    const demo = mode.value === 'demo';
    const preparing = demo && modePreparing.value;
    // A browser tab chooses for itself, a view of its own; the window asks the desktop, and a window that cannot shows the
    // switch off, with its sentence.
    const inTab = placeKind() !== 'window';
    const switchable = can('mode.set');
    return (
        <div className={`mode-strip ${demo ? 'mode-demo' : 'mode-live'}`} role="region" aria-label={t('mode.strip')}>
            {nav !== undefined && (
                <button
                    type="button"
                    className="mode-nav"
                    aria-label={t('mode.nav')}
                    aria-expanded={nav.open}
                    onClick={nav.toggle}
                >
                    <svg
                        width="16"
                        height="16"
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        strokeWidth="2"
                        strokeLinecap="round"
                        aria-hidden="true"
                    >
                        <path d="M4 6h16M4 12h16M4 18h16" />
                    </svg>
                </button>
            )}
            {demo && (
                <>
                    <span className="mode-dot" aria-hidden="true" />
                    <span className="mode-mark mode-strip-long">
                        <strong>{t('mode.strip.name')}</strong> {t('mode.strip.long')}
                    </span>
                    <span className="mode-strip-short">{t('mode.strip.short')}</span>
                    {preparing && (
                        <span className="mode-preparing" role="status" aria-label={t('mode.preparing')}>
                            {t('mode.preparing')}
                        </span>
                    )}
                </>
            )}
            {!demo && unopened !== null && (
                <span className="mode-unopened" role="status" aria-label={t('mode.unopened')}>
                    <span>{unopened}</span>
                    <button type="button" className="mode-unopened-close" onClick={() => (modeUnopened.value = null)}>
                        {t('mode.unopened.close')}
                    </button>
                </span>
            )}
            {refusal !== null && (
                <span className="mode-strip-refusal" role="alert">
                    {refusal}
                </span>
            )}
            <span className="mode-spacer" />
            <div className="mode-open-in" data-slot="open-in">
                {openIn}
            </div>
            <div className="mode-switch" role="group" aria-label={t('mode.switch')}>
                {modes.map((each) => (
                    <button
                        key={each}
                        type="button"
                        className={each === mode.value ? 'on' : undefined}
                        aria-pressed={each === mode.value}
                        disabled={inTab ? false : preparing || !switchable}
                        title={
                            inTab
                                ? each === 'demo'
                                    ? t('demo.show')
                                    : undefined
                                : !switchable
                                  ? cannotHere()
                                  : preparing
                                    ? t('mode.preparing.reason')
                                    : undefined
                        }
                        onClick={() => {
                            if (!inTab) void switchMode(each);
                            else if (each === 'demo') showDemo();
                        }}
                    >
                        {t(each === 'demo' ? 'mode.switch.demo' : 'mode.switch.live')}
                    </button>
                ))}
            </div>
        </div>
    );
}
