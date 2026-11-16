import { effect } from '@preact/signals-react';
import { useEffect, useRef, useState } from 'react';
import { href, navigate, route, type Route } from '../router';
import { demoFrame, demoStrip } from './demo-page';
import { t } from './i18n';

export { demoFrame, demoStrip };

// The demo in a browser tab: a view of that tab alone, never a mode of the desktop. The page is the one the desktop shows
// in Demo, carried by the UI's build under `sealed/` and served by the runtime unable to act; here it is shown in a frame
// sandboxed without the same origin, so it holds no storage of the runtime's origin, the UI's token among it, and reaches
// no address. The Demo | Live switch is drawn over the right end of the page's own top strip, where the desktop draws its
// own, so one page serves the window and the tab and a person sees one strip. The choice lives in the address (`#/demo`):
// nothing is kept, nothing is asked of the desktop, and the installation is unchanged.

/**
 * Whether the build the runtime serves carries the page: one read of it, as the frame would read it, stopped once its
 * status is read, so the page itself is downloaded once, by the frame.
 */
export const demoPage = {
    present: async (): Promise<boolean> => {
        const stop = new AbortController();
        try {
            return (await fetch(demoFrame.src, { cache: 'no-store', credentials: 'omit', signal: stop.signal })).ok;
        } catch {
            return false;
        } finally {
            stop.abort();
        }
    },
};

/** The console's address before the demo was shown, in this load's memory only; Live goes back to it. */
let before: string | null = null;

// An address that leaves the demo by itself, Back say, ends the visit the address was kept for: a later Forward to the
// demo and Live then go where the console opens, never to where an earlier visit began.
let inDemo = false;
effect(() => {
    const now = route.value.screen === 'demo';
    if (inDemo && !now) before = null;
    inDemo = now;
});

/** Shows the demo in this tab: the address moves to it, so Back leaves it. */
export function showDemo(): void {
    if (route.value.screen !== 'demo') before = href(route.value);
    navigate({ screen: 'demo' });
}

/** Leaves the demo for the console, where it was before, or the address the console opens at. */
export function leaveDemo(): void {
    const to = before;
    before = null;
    if (to !== null && to !== href({ screen: 'demo' })) {
        location.hash = to;
        return;
    }
    navigate({ screen: 'attention' } satisfies Route);
}

/**
 * The demo's view of the tab: the frame, filling it, and the switch over the right end of the page's strip. Narrower than
 * the page is ever shown in the desktop's window, the page keeps that width and scrolls sideways, and the switch stands
 * in a strip of its own above it, where it is always in reach.
 */
export function DemoView() {
    const [present, setPresent] = useState<boolean | null>(null);
    const view = useRef<HTMLDivElement>(null);
    useEffect(() => {
        let live = true;
        void demoPage.present().then((found) => {
            if (live) setPresent(found);
        });
        return () => {
            live = false;
        };
    }, []);
    useEffect(() => {
        // The page's geometry, set through the CSSOM: never a style attribute in markup.
        view.current?.style.setProperty('--demo-strip', `${demoStrip.height}px`);
        view.current?.style.setProperty('--demo-narrowest', `${demoStrip.narrowest}px`);
    }, []);
    return (
        <div ref={view} className="jc-console demo-view">
            <div className="demo-over">
                <div className="mode-switch" role="group" aria-label={t('mode.switch')}>
                    <button type="button" className="on" aria-pressed="true">
                        {t('mode.switch.demo')}
                    </button>
                    <button type="button" aria-pressed="false" title={t('demo.live')} onClick={leaveDemo}>
                        {t('mode.switch.live')}
                    </button>
                </div>
            </div>
            <div className="demo-stage">
                {present === true && (
                    <iframe
                        className="demo-frame"
                        src={demoFrame.src}
                        sandbox={demoFrame.sandbox}
                        allow={demoFrame.allow}
                        referrerPolicy={demoFrame.referrerPolicy}
                        loading={demoFrame.loading}
                        title={demoFrame.title}
                    />
                )}
                {present === false && (
                    <p className="demo-missing" role="status">
                        {t('demo.missing')}
                    </p>
                )}
            </div>
        </div>
    );
}
