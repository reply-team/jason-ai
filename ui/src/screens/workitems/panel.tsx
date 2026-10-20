import type { LoadMore } from '../../lists/use-cursor-list';
import { useState } from 'preact/hooks';
import type { Attempt, JournalEntry, WorkItem } from '../../api/types';
import { More } from '../../lists/more';
import { href } from '../../router';
import { clock, dayKey, dayLabel, relative } from '../../time/format';
import { DecideDialog } from '../attention/decide-dialog';
import { CancelDialog, type Ran } from './cancel-dialog';
import { creator, mark, plannedNote, what } from './labels';
import type { Patch } from './store';

type Props = {
    item: WorkItem;
    history: JournalEntry[];
    approvalId: string | null;
    notice: string | null;
    busy: boolean;
    now: Date;
    zone: string;
    moreHistory: LoadMore | null;
    onSave: (patch: Patch) => void;
    onCancel: (reason: string | undefined) => void;
    onApprove: (approval: string) => void;
    onReject: (approval: string, reason: string | undefined) => void;
    onClose: () => void;
};

/** Never left again: the runtime refuses to change succeeded, failed or cancelled work. */
const final = new Set(['succeeded', 'failed', 'cancelled']);

export function WorkItemPanel(props: Props) {
    const { item, history, approvalId, notice, busy, now, zone, moreHistory, onSave, onCancel, onClose } = props;
    const [cancelling, setCancelling] = useState(false);
    const [rejecting, setRejecting] = useState(false);
    const open = !final.has(item.status);
    const heading = item.contact_name ? `${what(item)} · ${item.contact_name}` : what(item);
    const m = mark(item.status);

    return (
        <div class="panel">
            <div class="panel-header">
                <div class="heading">
                    <h2>{heading}</h2>
                    <div class="sub">
                        in {item.campaign_name} · <span class={`mark ${m.kind}`}>{m.glyph}</span>{' '}
                        {item.status.replace('_', ' ')}
                    </div>
                </div>
                <button class="iconbtn close" type="button" title="Close" aria-label="Close" onClick={onClose}>
                    ×
                </button>
            </div>
            {open && (
                <div class="panel-actions">
                    <button class="btn danger" type="button" disabled={busy} onClick={() => setCancelling(true)}>
                        Cancel work
                    </button>
                </div>
            )}
            <div class="panel-body">
                {notice && <div class="notice">{notice}</div>}
                {item.status === 'awaiting_approval' && approvalId && (
                    <div class="section waiting">
                        <a class="notice" href={href({ screen: 'attention', kind: 'approvals', id: approvalId })}>
                            Waiting for your approval ›
                        </a>
                        <div class="panel-actions">
                            <button
                                class="btn primary"
                                type="button"
                                disabled={busy}
                                onClick={() => props.onApprove(approvalId)}
                            >
                                Approve
                            </button>
                            <button class="btn danger" type="button" disabled={busy} onClick={() => setRejecting(true)}>
                                Reject
                            </button>
                        </div>
                    </div>
                )}
                {item.status === 'failed' && <Failed item={item} now={now} zone={zone} />}
                <Facts item={item} now={now} zone={zone} />
                {open && <Edit key={`${item.id} ${item.updated_at}`} item={item} busy={busy} onSave={onSave} />}
                {(item.attempts?.length ?? 0) > 0 && (
                    <div class="section">
                        <h3>Attempts</h3>
                        {item.attempts!.map((attempt) => (
                            <AttemptRow key={attempt.id} attempt={attempt} zone={zone} />
                        ))}
                    </div>
                )}
                <details class="fold">
                    <summary>Input</summary>
                    <pre>{JSON.stringify(item.context, null, 2)}</pre>
                </details>
                {(item.external_reports?.length ?? 0) > 0 && (
                    <details class="fold">
                        <summary>Reported effects</summary>
                        {item.external_reports!.map((report) => (
                            <p key={report.id}>
                                {report.reporter.id ?? report.reporter.type} reported {report.effect}{' '}
                                {relative(new Date(report.received_at), now)}: {report.summary}
                            </p>
                        ))}
                    </details>
                )}
                {history.length > 0 && (
                    <div class="section history">
                        <h3>History</h3>
                        {history.map((entry) => (
                            <div key={entry.id} class="entry">
                                <span>{entryText(entry, zone)}</span>
                                <span class="by">
                                    {entry.actor.id ?? entry.actor.type} ·{' '}
                                    {dayLabel(dayKey(new Date(entry.ts), zone), dayKey(now, zone))}{' '}
                                    {clock(new Date(entry.ts), zone)}
                                    {entry.reason ? ` · ${entry.reason}` : ''}
                                </span>
                            </div>
                        ))}
                        <More more={moreHistory} />
                    </div>
                )}
            </div>
            {rejecting && approvalId && (
                <DecideDialog
                    verb="Reject"
                    what={heading}
                    onCancel={() => setRejecting(false)}
                    onConfirm={(reason) => {
                        setRejecting(false);
                        props.onReject(approvalId, reason);
                    }}
                />
            )}
            {cancelling && (
                <CancelDialog
                    what={heading}
                    ran={ranOf(item)}
                    onCancel={() => setCancelling(false)}
                    onConfirm={(reason) => {
                        setCancelling(false);
                        onCancel(reason);
                    }}
                />
            )}
        </div>
    );
}

function ranOf(item: WorkItem): Ran {
    if (item.status === 'processing' || item.status === 'scheduled') return 'running';
    return (item.attempts?.length ?? 0) === 0 ? 'never' : 'before';
}

/** What the runtime recorded about the failure: the error as it came, its class, the attempts against the limit. */
function Failed({ item, now, zone }: { item: WorkItem; now: Date; zone: string }) {
    const error = item.last_error;
    return (
        <div class="section failed">
            <h3>Failed</h3>
            <dl class="facts">
                {item.finished_at && (
                    <>
                        <dt>When</dt>
                        <dd>
                            {dayLabel(dayKey(new Date(item.finished_at), zone), dayKey(now, zone))}{' '}
                            {clock(new Date(item.finished_at), zone)}
                        </dd>
                    </>
                )}
                {error && (
                    <>
                        <dt>Error</dt>
                        <dd>
                            <code>{error.code}</code>
                        </dd>
                        <dt>Message</dt>
                        <dd>{error.message}</dd>
                        <dt>Class</dt>
                        <dd>{error.class ?? (error.retriable ? 'retriable' : 'not retriable')}</dd>
                    </>
                )}
                <dt>Attempts</dt>
                <dd>
                    {item.attempt_count} failed {item.attempt_count === 1 ? 'attempt' : 'attempts'}
                    {item.max_attempts !== null ? `, limit ${item.max_attempts}` : ', no limit'}
                </dd>
            </dl>
        </div>
    );
}

function Facts({ item, now, zone }: { item: WorkItem; now: Date; zone: string }) {
    const ran = item.attempts?.length ?? 0;
    const failed = item.attempt_count;
    const running = item.status === 'processing' ? item.attempts?.find((a) => a.status === 'running') : undefined;
    return (
        <dl class="facts">
            {item.contact_name && (
                <>
                    <dt>Person</dt>
                    <dd>{item.contact_name}</dd>
                </>
            )}
            <dt>Created</dt>
            <dd>
                {relative(new Date(item.created_at), now)} by {creator(item.created_by)}
            </dd>
            {item.status === 'created' && (
                <>
                    <dt>Runs</dt>
                    <dd>
                        {plannedNote(item, now, zone)}
                        {item.eligible ? ' · eligible now' : ''}
                    </dd>
                </>
            )}
            {running?.started_at && (
                <>
                    <dt>Running</dt>
                    <dd>since {clock(new Date(running.started_at), zone)}</dd>
                </>
            )}
            {/* A failed item says its attempts in the Failed block above. */}
            {item.status !== 'failed' && (
                <>
                    <dt>Attempts</dt>
                    <dd>
                        {ran} {ran === 1 ? 'run' : 'runs'}, {failed === 0 ? 'none failed' : `${failed} failed`}
                        {item.max_attempts !== null ? `, limit ${item.max_attempts}` : ', no limit'}
                    </dd>
                </>
            )}
            {item.execution_profile && (
                <>
                    <dt>Profile</dt>
                    <dd>{item.execution_profile}</dd>
                </>
            )}
        </dl>
    );
}

/** Priority, not before and due, as the runtime takes them; only what changed is sent. Remounted when the item changes. */
function Edit({ item, busy, onSave }: { item: WorkItem; busy: boolean; onSave: (patch: Patch) => void }) {
    const [priority, setPriority] = useState(String(item.priority));
    const [notBefore, setNotBefore] = useState(toLocal(item.not_before));
    const [due, setDue] = useState(toLocal(item.due_at));

    const patch: Patch = {};
    if (priority.trim() !== '' && Number(priority) !== item.priority) patch.priority = Number(priority);
    if (notBefore !== toLocal(item.not_before)) patch.not_before = fromLocal(notBefore);
    if (due !== toLocal(item.due_at)) patch.due_at = fromLocal(due);
    const changed = Object.keys(patch).length > 0;
    const running = item.status === 'scheduled' || item.status === 'processing';

    return (
        <div class="section">
            <h3>When it runs</h3>
            <div class="fields">
                <div class="field">
                    <label for="wi-priority">Priority</label>
                    <input
                        id="wi-priority"
                        class="input"
                        type="number"
                        value={priority}
                        onInput={(e) => setPriority((e.target as HTMLInputElement).value)}
                    />
                    <span class="hint">Higher runs first.</span>
                </div>
                <div class="field">
                    <label for="wi-not-before">Not before</label>
                    <input
                        id="wi-not-before"
                        class="input"
                        type="datetime-local"
                        value={notBefore}
                        onInput={(e) => setNotBefore((e.target as HTMLInputElement).value)}
                    />
                    <span class="hint">It does not start earlier.</span>
                </div>
                <div class="field">
                    <label for="wi-due">Due</label>
                    <input
                        id="wi-due"
                        class="input"
                        type="datetime-local"
                        value={due}
                        onInput={(e) => setDue((e.target as HTMLInputElement).value)}
                    />
                    <span class="hint">Not started by then, it expires.</span>
                </div>
            </div>
            {item.status === 'expired' && <p class="hint">A future due date puts this back in the queue.</p>}
            {running && <p class="hint">Takes effect at the next scan.</p>}
            <div class="btn-row">
                <button class="btn primary" type="button" disabled={busy || !changed} onClick={() => onSave(patch)}>
                    Save
                </button>
            </div>
        </div>
    );
}

function AttemptRow({ attempt, zone }: { attempt: Attempt; zone: string }) {
    const m = mark(attempt.status === 'running' ? 'processing' : attempt.status);
    const started = attempt.started_at ? new Date(attempt.started_at) : null;
    const finished = attempt.finished_at ? new Date(attempt.finished_at) : null;
    const seconds =
        started && finished ? Math.max(0, Math.round((finished.getTime() - started.getTime()) / 1000)) : null;
    const took = seconds === null ? null : seconds < 60 ? `${seconds} s` : `${Math.round(seconds / 60)} min`;
    const plugin = attempt.provenance?.plugin_id
        ? `${attempt.provenance.plugin_id} ${attempt.provenance.plugin_version ?? ''}`.trim()
        : null;
    return (
        <div class="attempt">
            <div class="line">
                <span class={`mark ${m.kind}`}>{m.glyph}</span>
                <span>Attempt {attempt.number}</span>
                <span class="when">
                    {started ? clock(started, zone) : 'claimed'}
                    {finished ? `–${clock(finished, zone)}` : ''}
                    {took ? ` · ${took}` : ''}
                </span>
                {attempt.launch?.exit_code !== undefined && attempt.launch?.exit_code !== null && (
                    <span class="when">exit code {attempt.launch.exit_code}</span>
                )}
            </div>
            {attempt.error && (
                <div class="error">
                    <code>{attempt.error.code}</code>
                    <span>{attempt.error.message}</span>
                </div>
            )}
            {(plugin || attempt.execution_profile) && (
                <div class="hint">
                    {plugin ? `${plugin} plugin` : `profile ${attempt.execution_profile}`}
                    {attempt.provenance?.operation ? ` · ${attempt.provenance.operation}` : ''}
                </div>
            )}
        </div>
    );
}

const kindWords: Record<string, string> = {
    workitem_created: 'Created',
    workitem_updated: 'Changed',
    workitem_context_updated: 'Context changed',
    workitem_scheduled: 'Claimed',
    workitem_processing: 'Started',
    workitem_succeeded: 'Succeeded',
    workitem_failed: 'Failed',
    workitem_cancelled: 'Cancelled',
    workitem_expired: 'Expired',
    workitem_released: 'Released for another attempt',
    workitem_awaiting_approval: 'Parked for approval',
    workitem_reopened: 'Reopened',
};

function entryText(entry: JournalEntry, zone: string): string {
    const words = kindWords[entry.kind] ?? entry.kind.replaceAll('_', ' ');
    if (!entry.key) return words;
    return `${words} · ${entry.key}: ${value(entry.old, zone)} → ${value(entry.new, zone)}`;
}

function value(raw: unknown, zone: string): string {
    if (raw === null || raw === undefined) return 'none';
    if (typeof raw === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(raw)) {
        const at = new Date(raw);
        return `${dayKey(at, zone)} ${clock(at, zone)}`;
    }
    return typeof raw === 'string' ? raw : JSON.stringify(raw);
}

/** A moment as a date-and-time field shows it: the person's own clock, to the minute. */
function toLocal(iso: string | null): string {
    if (!iso) return '';
    const at = new Date(iso);
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}T${pad(at.getHours())}:${pad(at.getMinutes())}`;
}

function fromLocal(local: string): string | null {
    return local ? new Date(local).toISOString() : null;
}
