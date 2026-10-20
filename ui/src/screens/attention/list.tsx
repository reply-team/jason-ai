import type { LoadMore } from '../../lists/use-cursor-list';
import { More } from '../../lists/more';
import { navigate, route } from '../../router';
import { relative } from '../../time/format';
import type { ApprovalSummary, DecisionSummary } from '../../api/types';
import { approvalMark, approvalNote, title } from './labels';
import type { Kind } from './store';

type Props = {
    approvals: ApprovalSummary[];
    questions: DecisionSummary[];
    moreApprovals: LoadMore | null;
    moreQuestions: LoadMore | null;
    now: Date;
};

export function AttentionList({ approvals, questions, moreApprovals, moreQuestions, now }: Props) {
    const selected = route.value.screen === 'attention' ? route.value : null;
    const isSelected = (kind: Kind, id: string) => selected?.kind === kind && selected.id === id;
    return (
        <div class="rows">
            {approvals.length > 0 && <div class="group first">Approvals</div>}
            {approvals.map((a) => (
                <a
                    key={a.id}
                    class={`row${isSelected('approvals', a.id) ? ' selected' : ''}`}
                    href="#"
                    onClick={(e) => {
                        e.preventDefault();
                        navigate({ screen: 'attention', kind: 'approvals', id: a.id });
                    }}
                >
                    <span class={`mark ${approvalMark(a.status).kind}`}>{approvalMark(a.status).glyph}</span>
                    <span class="what">
                        <span class="name">{title(a.operation, a.contact_name)}</span>
                        <span class="in">in {a.campaign_name}</span>
                    </span>
                    <span class="meta">
                        {approvalNote(a.status, a.decided_by?.id) && (
                            <span>{approvalNote(a.status, a.decided_by?.id)}</span>
                        )}
                        <time>{relative(new Date(a.decided_at ?? a.requested_at), now)}</time>
                    </span>
                </a>
            ))}
            <More more={moreApprovals} />
            {questions.length > 0 && <div class={`group${approvals.length > 0 ? '' : ' first'}`}>Questions</div>}
            {questions.map((q) => (
                <a
                    key={q.id}
                    class={`row${isSelected('questions', q.id) ? ' selected' : ''}`}
                    href="#"
                    onClick={(e) => {
                        e.preventDefault();
                        navigate({ screen: 'attention', kind: 'questions', id: q.id });
                    }}
                >
                    <span class={`mark ${q.status === 'pending' ? 'ask' : 'off'}`}>?</span>
                    <span class="what">
                        <span class="name">{q.question}</span>
                        <span class="in">{q.campaign_name}</span>
                    </span>
                    <span class="meta">
                        {q.answered_by?.id && <span>answered by {q.answered_by.id}</span>}
                        <time>{relative(new Date(q.answered_at ?? q.raised_at), now)}</time>
                    </span>
                </a>
            ))}
            <More more={moreQuestions} />
        </div>
    );
}
