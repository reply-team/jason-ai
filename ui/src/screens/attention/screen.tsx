import { useEffect, useState } from 'react';
import { useApi } from '../../api/context';
import { FiltersBar } from '../../layout/filters-bar';
import { Segmented } from '../../layout/segmented';
import { t } from '../../platform/i18n';
import { Toolbar } from '../../layout/toolbar';
import { navigate, route } from '../../router';
import { ApprovalPanel } from './approval-panel';
import { AttentionList } from './list';
import { QuestionPanel } from './question-panel';
import * as store from './store';
import { Notice } from '../../layout/notice';

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

/** Which of the listed items the screen shows: all of them, or the one kind. */
type Shows = 'all' | store.Kind;

export function AttentionScreen() {
    const api = useApi();
    const current = route.value;
    const selected =
        current.screen === 'attention' && current.kind && current.id ? { kind: current.kind, id: current.id } : null;
    // Pending or Decided lives in the address, so a refresh or a link keeps it.
    const decided = current.screen === 'attention' && current.decided === true;
    const keep = decided ? { decided: true as const } : {};

    useEffect(() => {
        void store.load(api, decided);
    }, [api, decided]);

    useEffect(() => {
        if (selected) void store.open(api, selected.kind, selected.id);
        else store.detail.value = null;
    }, [api, selected?.kind, selected?.id]);

    // The filter is the screen's own: kept while it is open, never stored, in the address or anywhere else, and never
    // sent to the runtime. When nothing is listed at all it goes back to everything, so what arrives next is shown.
    const [shows, setShows] = useState<Shows>('all');
    const count = store.approvals.value.length + store.questions.value.length;
    const nothing = store.loaded.value && count === 0;
    useEffect(() => {
        if (nothing) setShows('all');
    }, [nothing]);
    const only = shows === 'all' ? undefined : shows;
    const approvals = shows === 'questions' ? [] : store.approvals.value;
    const questions = shows === 'approvals' ? [] : store.questions.value;

    // A count is of what is loaded: where the runtime holds more pages of a kind, its option says so.
    const moreApprovalsWait = !!store.approvalsCursor.value;
    const moreQuestionsWait = !!store.questionsCursor.value;
    const loadedNote = t('attention.filter.loaded');

    const now = new Date();
    const detail = store.detail.value;
    const remaining = selected ? remainingText(selected, only) : '';

    const go = (to: { kind: store.Kind; id: string } | null) =>
        navigate(to ? { screen: 'attention', kind: to.kind, id: to.id, ...keep } : { screen: 'attention', ...keep });
    const close = () => navigate({ screen: 'attention', ...keep });
    // An open item stays open under its own kind and under All; under the other kind its panel closes with the list.
    const choose = (next: Shows) => {
        setShows(next);
        if (selected && next !== 'all' && next !== selected.kind) close();
    };

    // The next item is chosen before the action, from the list as it is; after the reload it is the one the person lands on.
    const decide = (acted: { kind: store.Kind; id: string }, call: () => Promise<boolean>) => {
        const planned = store.nextPending(acted, only);
        void call().then((ok) => {
            if (ok) go(store.stillPending(planned, only));
        });
    };

    return (
        <div className={`main${selected ? '' : ' single'}`}>
            <div className="list">
                <Toolbar
                    title="Attention"
                    refresh={{ busy: store.refreshing.value, onRefresh: () => void store.refresh(api, decided) }}
                >
                    <Segmented
                        label="Show"
                        options={[
                            { value: false, label: 'Pending' },
                            { value: true, label: 'Decided' },
                        ]}
                        value={decided}
                        onChange={(on) =>
                            navigate(on ? { screen: 'attention', decided: true } : { screen: 'attention' })
                        }
                    />
                </Toolbar>
                {store.failure.value && <Notice danger>{store.failure.value}</Notice>}
                {store.loaded.value && count > 0 && (
                    <FiltersBar>
                        <Segmented
                            label={t('attention.filter.label')}
                            options={[
                                {
                                    value: 'all',
                                    label: t('attention.filter.all'),
                                    count,
                                    note: moreApprovalsWait || moreQuestionsWait ? loadedNote : undefined,
                                },
                                {
                                    value: 'approvals',
                                    label: t('attention.filter.approvals'),
                                    count: store.approvals.value.length,
                                    note: moreApprovalsWait ? loadedNote : undefined,
                                },
                                {
                                    value: 'questions',
                                    label: t('attention.filter.questions'),
                                    count: store.questions.value.length,
                                    note: moreQuestionsWait ? loadedNote : undefined,
                                },
                            ]}
                            value={shows}
                            onChange={choose}
                            tick
                        />
                        <span className="shown" role="status">
                            {t('attention.filter.shown', { count: String(approvals.length + questions.length) })}
                        </span>
                    </FiltersBar>
                )}
                {nothing ? (
                    <div className="empty">
                        <strong>Nothing needs you</strong>
                        {!decided ? 'No approval is waiting and no question is open.' : 'Nothing has been decided yet.'}
                    </div>
                ) : (
                    <>
                        {shows !== 'all' && approvals.length + questions.length === 0 && (
                            <div className="empty">{noneOfKind(shows, decided)}</div>
                        )}
                        <AttentionList
                            approvals={approvals}
                            questions={questions}
                            moreApprovals={
                                shows !== 'questions' && store.approvalsCursor.value
                                    ? () => store.more(api, 'approvals', decided)
                                    : null
                            }
                            moreQuestions={
                                shows !== 'approvals' && store.questionsCursor.value
                                    ? () => store.more(api, 'questions', decided)
                                    : null
                            }
                            now={now}
                        />
                    </>
                )}
            </div>
            {selected && detail?.kind === 'approvals' && (
                <ApprovalPanel
                    approval={detail.item}
                    now={now}
                    zone={zone}
                    busy={store.busy.value}
                    remaining={remaining}
                    onApprove={() =>
                        decide({ kind: 'approvals', id: detail.item.id }, () => store.approve(api, detail.item.id))
                    }
                    onReject={(reason) =>
                        decide({ kind: 'approvals', id: detail.item.id }, () =>
                            store.reject(api, detail.item.id, reason),
                        )
                    }
                    onSkip={() => go(store.nextPending(selected, only))}
                    onClose={close}
                    onReasked={(id) => {
                        void store.load(api, decided);
                        go({ kind: 'approvals', id });
                    }}
                />
            )}
            {selected && detail?.kind === 'questions' && (
                <QuestionPanel
                    decision={detail.item}
                    now={now}
                    busy={store.busy.value}
                    remaining={remaining}
                    onAnswer={(choice, reason) =>
                        decide({ kind: 'questions', id: detail.item.id }, () =>
                            store.answer(api, detail.item.id, choice, reason),
                        )
                    }
                    onSkip={() => go(store.nextPending(selected, only))}
                    onClose={close}
                />
            )}
        </div>
    );
}

/** The line in the place of a kind the filter shows when none of that kind is listed. */
function noneOfKind(kind: store.Kind, decided: boolean): string {
    if (kind === 'approvals') return t(decided ? 'attention.filter.noneDecided' : 'attention.filter.noneWaiting');
    return t(decided ? 'attention.filter.noneAnswered' : 'attention.filter.noneOpen');
}

/** What waits after the open item, of the kind the filter shows, or of both under All. */
function remainingText(selected: { kind: store.Kind; id: string }, only: store.Kind | undefined): string {
    const a =
        only === 'questions'
            ? 0
            : store.approvals.value.filter((x) => !(selected.kind === 'approvals' && x.id === selected.id)).length;
    const q =
        only === 'approvals'
            ? 0
            : store.questions.value.filter((x) => !(selected.kind === 'questions' && x.id === selected.id)).length;
    if (a + q === 0) return only ? `No more ${only} after this one` : 'Nothing else is waiting';
    const parts = [
        a > 0 ? `${a} more approval${a === 1 ? '' : 's'}` : null,
        q > 0 ? `${q} question${q === 1 ? '' : 's'}` : null,
    ].filter(Boolean);
    return `${parts.join(' and ')} after this one`;
}
