import { useEffect, useState } from 'react';
import { Unpaired, type Api } from '../api/client';
import { session } from '../api/session';
import type { ApprovalSummary, DecisionSummary, Page } from '../api/types';
import { showDemo } from '../platform/demo-frame';
import { choose, chosen, opening } from '../platform/tab-choice';
import { replace } from '../router';
import { t } from '../platform/i18n';
import { releasesPage } from '../platform/places';

/** What waits for a person, as the two lists Needs you reads say it: a count of each, and whether a list goes on. */
interface Waiting {
    approvals: number;
    questions: number;
    more: boolean;
}

async function readWaiting(api: Api): Promise<Waiting | null> {
    try {
        const [approvals, questions] = await Promise.all([
            api.call<Page<ApprovalSummary>>('approval.list', {}),
            api.call<Page<DecisionSummary>>('decision.list', {}),
        ]);
        return {
            approvals: approvals.items.length,
            questions: questions.items.length,
            more: approvals.next_cursor !== null || questions.next_cursor !== null,
        };
    } catch (error) {
        if (error instanceof Unpaired) session.clear();
        return null;
    }
}

/** The quiet line under the page's words: what waits, and where to decide it. Nothing when nothing waits or the read failed. */
export function waitingLine(waiting: Waiting | null): string | null {
    if (waiting === null || waiting.approvals + waiting.questions === 0) return null;
    const figure = (n: number) => `${n}${waiting.more ? '+' : ''}`;
    const approvals =
        waiting.approvals === 0
            ? null
            : t(waiting.approvals === 1 ? 'noDesktop.approval.one' : 'noDesktop.approval.many', {
                  n: figure(waiting.approvals),
              });
    const questions =
        waiting.questions === 0
            ? null
            : t(waiting.questions === 1 ? 'noDesktop.question.one' : 'noDesktop.question.many', {
                  n: figure(waiting.questions),
              });
    const what =
        approvals !== null && questions !== null
            ? t('noDesktop.both', { approvals, questions })
            : (approvals ?? questions ?? '');
    const single = waiting.approvals + waiting.questions === 1;
    return t(single ? 'noDesktop.waiting.one' : 'noDesktop.waiting.many', { what, where: t('noDesktop.where') });
}

/**
 * A browser tab with no desktop: it opens on the Demo, or, while something waits, on one page of text, centred, with the
 * console's Demo | Live switch at the top right. The console is never drawn here, whatever the installation holds and
 * whatever the address names. Under the words a quiet line says what waits, only while something does.
 */
export function NoDesktopPage({ api }: { api: Api }) {
    // undefined: the read has not answered; null: it failed.
    const [waiting, setWaiting] = useState<Waiting | null | undefined>(undefined);
    // What the tab opens on is decided once per load (see `opening`); the person's own choice outranks what waits.
    const [first] = useState(() => !opening.done);
    // A press of Live while the read is pending is held here, so the page is drawn at once, answered or not.
    const [pressed, setPressed] = useState(false);
    const own = pressed ? 'live' : first ? chosen() : 'live';
    useEffect(() => {
        let live = true;
        void readWaiting(api).then((found) => {
            if (live) setWaiting(found);
        });
        return () => {
            live = false;
        };
    }, [api]);
    // With no choice made, nothing waiting opens the Demo, as a press of Demo does, though it is not the person's choice
    // and it replaces the address, so Back leaves the Demo for where the tab came from.
    const toDemo =
        own === 'demo' || (own === null && waiting !== undefined && waiting !== null && waitingLine(waiting) === null);
    useEffect(() => {
        if (toDemo) {
            opening.done = true;
            replace({ screen: 'demo' });
        }
    }, [toDemo]);
    const settled = own === 'live' || (waiting !== undefined && !toDemo);
    useEffect(() => {
        if (settled) opening.done = true;
    }, [settled]);
    const line = waitingLine(waiting ?? null);
    const [before, after] = t('noDesktop.download').split('{link}');
    return (
        <div className="jc-console no-desktop">
            <div className="no-desktop-top">
                <div className="mode-switch" role="group" aria-label={t('mode.switch')}>
                    <button type="button" aria-pressed="false" title={t('demo.show')} onClick={showDemo}>
                        {t('mode.switch.demo')}
                    </button>
                    <button
                        type="button"
                        className={settled ? 'on' : undefined}
                        aria-pressed={settled ? 'true' : 'false'}
                        onClick={() => {
                            choose('live');
                            setPressed(true);
                        }}
                    >
                        {t('mode.switch.live')}
                    </button>
                </div>
            </div>
            {settled && (
                <main className="no-desktop-page">
                    <h1>{t('noDesktop.title')}</h1>
                    <p>
                        {before}
                        <a href={releasesPage} target="_blank" rel="noopener noreferrer">
                            {t('noDesktop.link')}
                        </a>
                        {after}
                    </p>
                    <p>{t('noDesktop.alternative')}</p>
                    {line !== null && <p className="no-desktop-waiting">{line}</p>}
                </main>
            )}
        </div>
    );
}
