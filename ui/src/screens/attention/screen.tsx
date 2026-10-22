import { useEffect } from 'preact/hooks';
import { useApi } from '../../api/context';
import { navigate, route } from '../../router';
import { ApprovalPanel } from './approval-panel';
import { AttentionList } from './list';
import { QuestionPanel } from './question-panel';
import * as store from './store';

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

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

    const now = new Date();
    const detail = store.detail.value;
    const count = store.approvals.value.length + store.questions.value.length;
    const remaining = selected ? remainingText(selected) : '';

    const go = (to: { kind: store.Kind; id: string } | null) =>
        navigate(to ? { screen: 'attention', kind: to.kind, id: to.id, ...keep } : { screen: 'attention', ...keep });
    const close = () => navigate({ screen: 'attention', ...keep });

    // The next item is chosen before the action, from the list as it is; after the reload it is the one the person lands on.
    const decide = (acted: { kind: store.Kind; id: string }, call: () => Promise<boolean>) => {
        const planned = store.nextPending(acted);
        void call().then((ok) => {
            if (ok) go(store.stillPending(planned));
        });
    };

    return (
        <div class={`main${selected ? '' : ' single'}`}>
            <div class="list">
                <div class="toolbar">
                    <h1>Attention</h1>
                    <div class="segmented" role="group" aria-label="Show">
                        {(['pending', 'decided'] as const).map((f) => (
                            <button
                                key={f}
                                type="button"
                                class={decided === (f === 'decided') ? 'on' : ''}
                                aria-pressed={decided === (f === 'decided')}
                                onClick={() => {
                                    if (decided === (f === 'decided')) return;
                                    navigate(
                                        f === 'decided'
                                            ? { screen: 'attention', decided: true }
                                            : { screen: 'attention' },
                                    );
                                }}
                            >
                                {f === 'pending' ? 'Pending' : 'Decided'}
                            </button>
                        ))}
                    </div>
                    <button
                        class={`iconbtn${store.refreshing.value ? ' spin' : ''}`}
                        type="button"
                        title="Refresh"
                        disabled={store.refreshing.value}
                        onClick={() => void store.refresh(api, decided)}
                    >
                        <span class="glyph">⟳</span>
                    </button>
                </div>
                {store.failure.value && (
                    <div class="notice danger" role="alert">
                        {store.failure.value}
                    </div>
                )}
                {store.loaded.value && count === 0 ? (
                    <div class="empty">
                        <strong>Nothing needs you</strong>
                        {!decided ? 'No approval is waiting and no question is open.' : 'Nothing has been decided yet.'}
                    </div>
                ) : (
                    <AttentionList
                        approvals={store.approvals.value}
                        questions={store.questions.value}
                        moreApprovals={store.approvalsCursor.value ? () => store.more(api, 'approvals', decided) : null}
                        moreQuestions={store.questionsCursor.value ? () => store.more(api, 'questions', decided) : null}
                        now={now}
                    />
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
                    onSkip={() => go(store.nextPending(selected))}
                    onClose={close}
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
                    onSkip={() => go(store.nextPending(selected))}
                    onClose={close}
                />
            )}
        </div>
    );
}

function remainingText(selected: { kind: store.Kind; id: string }): string {
    const a = store.approvals.value.filter((x) => !(selected.kind === 'approvals' && x.id === selected.id)).length;
    const q = store.questions.value.filter((x) => !(selected.kind === 'questions' && x.id === selected.id)).length;
    if (a + q === 0) return 'Nothing else is waiting';
    const parts = [
        a > 0 ? `${a} more approval${a === 1 ? '' : 's'}` : null,
        q > 0 ? `${q} question${q === 1 ? '' : 's'}` : null,
    ].filter(Boolean);
    return `${parts.join(' and ')} after this one`;
}
