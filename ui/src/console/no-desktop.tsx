import { useEffect, useState } from 'react';
import { Unpaired, type Api } from '../api/client';
import { session } from '../api/session';
import type { ApprovalSummary, DecisionSummary, Page } from '../api/types';
import { showDemo } from '../platform/demo-frame';
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
 * A browser tab with no desktop: one page of text, centred, and the console's Demo | Live switch at the top right. The
 * console is never drawn here, whatever the installation holds and whatever the address names; the address is left as it
 * is. Under the words a quiet line says what waits, only while something does.
 */
export function NoDesktopPage({ api }: { api: Api }) {
    const [waiting, setWaiting] = useState<Waiting | null>(null);
    useEffect(() => {
        let live = true;
        void readWaiting(api).then((found) => {
            if (live) setWaiting(found);
        });
        return () => {
            live = false;
        };
    }, [api]);
    const line = waitingLine(waiting);
    const [before, after] = t('noDesktop.download').split('{link}');
    return (
        <div className="jc-console no-desktop">
            <div className="no-desktop-top">
                <div className="mode-switch" role="group" aria-label={t('mode.switch')}>
                    <button type="button" aria-pressed="false" title={t('demo.show')} onClick={showDemo}>
                        {t('mode.switch.demo')}
                    </button>
                    <button type="button" className="on" aria-pressed="true">
                        {t('mode.switch.live')}
                    </button>
                </div>
            </div>
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
        </div>
    );
}
