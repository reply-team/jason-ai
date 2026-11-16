import { useEffect, useRef, useState } from 'react';
import { ApiError, Unpaired, type Api } from '../../api/client';
import { useApi } from '../../api/context';
import { session } from '../../api/session';
import type {
    ApprovalSummary,
    Campaign,
    DecisionSummary,
    Page,
    RouteResolution,
    WorkItemSummary,
} from '../../api/types';
import { Dialog } from '../../layout/dialog';
import { Notice } from '../../layout/notice';
import { useCampaignChat } from './campaign-chat';
import { unfinishedStatuses } from './kind';
import { provider } from './pause-dialog';
import * as store from './store';

/** One page of each read, as large as a person's agent holds open; the reads page on until the runtime says no more. */
const pageSize = 200;

/** Every page of a list the runtime pages with a cursor. */
async function everyPage<T>(api: Api, operation: string, body: Record<string, unknown>): Promise<T[]> {
    const items: T[] = [];
    let cursor: string | null = null;
    do {
        const page: Page<T> = await api.call<Page<T>>(operation, cursor ? { ...body, cursor } : body);
        items.push(...page.items);
        cursor = page.next_cursor;
    } while (cursor);
    return items;
}

/**
 * What an archive cancels, as the runtime answers when the confirmation opens: the open work (and how much of it runs
 * now), the approvals on that work still asked and already given, and the questions pending. Null where a read failed.
 */
type Cancels = { work: number; running: number; asked: number; given: number; questions: number };

/** Whether Jason itself can pause the provider's campaign before the archive: the campaign's route for the pause. */
type Before = { name: string; pin: string; pausable: boolean };

type Read =
    | { kind: 'archived'; record: Campaign }
    | { kind: 'open'; record: Campaign; cancels: Cancels | null; before: Before | null };

async function readCancels(api: Api, id: string): Promise<Cancels> {
    const [work, approvals, questions] = await Promise.all([
        everyPage<WorkItemSummary>(api, 'workitem.list', {
            campaign_id: id,
            status: [...unfinishedStatuses],
            order: 'created',
            limit: pageSize,
        }),
        everyPage<ApprovalSummary>(api, 'approval.list', {
            campaign_id: id,
            statuses: ['pending', 'approved'],
            limit: pageSize,
        }),
        everyPage<DecisionSummary>(api, 'decision.list', { campaign_id: id, status: 'pending', limit: pageSize }),
    ]);
    // An approval cancelled with its work is one whose work is open; an approval given to work that has run is left alone.
    const open = new Set(work.map((item) => item.id));
    const onOpen = approvals.filter((approval) => open.has(approval.work_item_id));
    return {
        work: work.length,
        running: work.filter((item) => item.status === 'processing').length,
        asked: onOpen.filter((approval) => approval.status === 'pending').length,
        given: onOpen.filter((approval) => approval.status === 'approved').length,
        questions: questions.length,
    };
}

/** The provider's campaign this agent is linked to, and whether this agent's route lets Jason pause it there first. */
async function readBefore(api: Api, record: Campaign): Promise<Before | null> {
    const pin = (record.external_ids ?? []).find((p) => p.kind === 'campaign');
    const name = provider(record);
    if (!pin || !name) return null;
    // Work for the provider runs in an agent that is live or paused; a draft's waits, so it pauses nothing first.
    if (record.status !== 'active' && record.status !== 'paused') return { name, pin: pin.value, pausable: false };
    try {
        const route = await api.call<RouteResolution>('route.resolve', {
            campaign_id: record.id,
            operation: 'campaign.pause',
        });
        return { name, pin: pin.value, pausable: route.usable };
    } catch (error) {
        if (error instanceof Unpaired) throw error;
        return { name, pin: pin.value, pausable: false };
    }
}

/** Reads the agent as it is now and, unless it is archived already, what archiving it cancels. */
async function readArchive(api: Api, id: string): Promise<Read> {
    const record = await api.call<Campaign>('campaign.get', { campaign_id: id });
    if (record.status === 'archived') return { kind: 'archived', record };
    const [cancels, before] = await Promise.allSettled([readCancels(api, id), readBefore(api, record)]);
    for (const read of [cancels, before])
        if (read.status === 'rejected' && read.reason instanceof Unpaired) throw read.reason;
    return {
        kind: 'open',
        record,
        cancels: cancels.status === 'fulfilled' ? cancels.value : null,
        before: before.status === 'fulfilled' ? before.value : null,
    };
}

const number = new Intl.NumberFormat('en-US');

/** "3 open work items": a count and its noun, one or many. */
function many(count: number, one: string, more: string): string {
    return `${number.format(count)} ${count === 1 ? one : more}`;
}

/** What archiving cancels, from the counts read as the confirmation opened. Text only. */
function WhatArchivingCancels({ cancels }: { cancels: Cancels | null }) {
    if (cancels === null)
        return (
            <section className="section archive-cancels" aria-label="What archiving cancels">
                <p className="muted">
                    What this agent has open could not be read. Archiving cancels it all the same: its open work, the
                    approvals on that work and its questions.
                </p>
            </section>
        );
    const { work, running, asked, given, questions } = cancels;
    if (work + asked + given + questions === 0)
        return (
            <section className="section archive-cancels" aria-label="What archiving cancels">
                <p>Nothing is open: no work, no approval and no question is waiting.</p>
            </section>
        );
    return (
        <section className="section archive-cancels" aria-label="What archiving cancels">
            <p>Archiving cancels:</p>
            <ul>
                {work > 0 && (
                    <li>
                        {many(work, 'open work item', 'open work items')}
                        {running > 0 &&
                            `, ${running === 1 ? 'one of them' : `${number.format(running)} of them`} running now and stopped`}
                    </li>
                )}
                {asked > 0 && <li>{many(asked, 'approval still asked of you', 'approvals still asked of you')}</li>}
                {given > 0 && (
                    <li>
                        {many(given, 'approval already given', 'approvals already given')}, whose work then never runs
                    </li>
                )}
                {questions > 0 && (
                    <li>{many(questions, 'question waiting for an answer', 'questions waiting for an answer')}</li>
                )}
            </ul>
            {work > 0 && <p className="muted">Only a search being stopped still finishes.</p>}
        </section>
    );
}

/** What archiving leaves at the provider, and the way to stop it there first that is true for this agent. */
function TheProvidersCampaign({ before, chat }: { before: Before; chat: boolean }) {
    const { name, pin, pausable } = before;
    return (
        <section className="section archive-provider" aria-label={`${name}'s campaign`}>
            <p>
                {name}'s campaign {pin} is left as it is: if it is live there, it keeps sending on its own schedule, and
                once this agent is archived Jason cannot pause it.
            </p>
            <p>
                {pausable && chat
                    ? `To stop it first, ask in this agent's chat to pause it in ${name}, and archive once that work has finished; or pause it in ${name} itself.`
                    : `To stop it first, pause it in ${name} itself, then archive.`}
            </p>
        </section>
    );
}

type DialogProps = { campaign: Campaign; onDone: (archived: Campaign) => void; onClose: () => void };

/** The confirmation: the agent named, that it cannot be undone, and what it cancels, read as it opens. */
export function ArchiveDialog({ campaign, onDone, onClose }: DialogProps) {
    const api = useApi();
    const chat = useCampaignChat() !== null;
    const [read, setRead] = useState<Read | null>(null);
    const [failed, setFailed] = useState(false);
    const [reason, setReason] = useState('');
    const [busy, setBusy] = useState(false);
    const [refused, setRefused] = useState<string | null>(null);
    // One press is one archive: a second press before the answer, a double click, sends nothing more.
    const sending = useRef(false);
    useEffect(() => {
        let current = true;
        readArchive(api, campaign.id).then(
            (answer) => {
                if (current) setRead(answer);
            },
            (error: unknown) => {
                if (!current) return;
                if (error instanceof Unpaired) session.clear();
                else setFailed(true);
            },
        );
        return () => {
            current = false;
        };
    }, [api, campaign.id]);

    const act = async () => {
        if (sending.current) return;
        sending.current = true;
        setBusy(true);
        setRefused(null);
        try {
            onDone(await store.archive(api, campaign.id, reason.trim() || undefined));
        } catch (error) {
            sending.current = false;
            if (error instanceof Unpaired) session.clear();
            else setRefused(error instanceof ApiError ? error.message : String(error));
        } finally {
            setBusy(false);
        }
    };

    const label = `Archive ${campaign.name}`;
    if (read?.kind === 'archived')
        return (
            <Dialog title={label} label={label} onClose={onClose} dismiss="Close" buttons={null}>
                <p>This agent is archived already, from another window or a terminal. Nothing more is done.</p>
            </Dialog>
        );
    return (
        <Dialog
            title={label}
            label={label}
            onClose={onClose}
            buttons={
                <button
                    className="btn danger"
                    type="button"
                    disabled={busy || (read === null && !failed)}
                    onClick={() => void act()}
                >
                    Archive
                </button>
            }
        >
            <p>An archive cannot be undone: nothing brings this agent back.</p>
            {read === null && !failed && <p className="muted">Reading what this agent has open…</p>}
            {failed && <WhatArchivingCancels cancels={null} />}
            {read?.kind === 'open' && <WhatArchivingCancels cancels={read.cancels} />}
            {read?.kind === 'open' && read.before && <TheProvidersCampaign before={read.before} chat={chat} />}
            <div className="field">
                <label htmlFor="archive-reason">Reason (optional, kept in the journal)</label>
                <textarea
                    id="archive-reason"
                    className="textarea"
                    aria-label="Reason"
                    maxLength={2000}
                    value={reason}
                    onChange={(e) => setReason((e.target as HTMLTextAreaElement).value)}
                />
            </div>
            {refused && <Notice danger>{refused}</Notice>}
        </Dialog>
    );
}

type SectionProps = { campaign: Campaign; onArchived: (archived: Campaign) => void };

/**
 * The last part of the agent's own Settings: one sentence and the Archive button that opens its confirmation. The act is
 * the person's own, made only by the button and the confirmation; an archived agent's part says so and offers nothing.
 */
export function ArchiveSection({ campaign, onArchived }: SectionProps) {
    const [open, setOpen] = useState<string | null>(null);
    if (campaign.status === 'archived')
        return (
            <div className="section archive-agent">
                <h3>Archive</h3>
                <p className="hint">This agent is archived, and an archive cannot be undone.</p>
            </div>
        );
    return (
        <div className="section archive-agent">
            <h3>Archive this agent</h3>
            <p className="hint">
                Archiving ends this agent for good and cannot be undone: its open work, its approvals and its questions
                are cancelled.
            </p>
            <div className="btn-row">
                <button className="btn danger" type="button" onClick={() => setOpen(campaign.id)}>
                    Archive
                </button>
            </div>
            {open === campaign.id && (
                <ArchiveDialog
                    campaign={campaign}
                    onDone={(archived) => {
                        setOpen(null);
                        onArchived(archived);
                    }}
                    onClose={() => setOpen(null)}
                />
            )}
        </div>
    );
}
