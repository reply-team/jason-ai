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
import { countCancels, jasonMayPauseFirst, many, nothingOpen, type Cancels } from './archive-model';
import { useCampaignChat } from './campaign-chat';
import { unfinishedStatuses } from './kind';
import { provider, type Movable } from './pause-dialog';
import * as store from './store';
import { ReasonField } from '../../layout/reason-field';

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

/** The counts, or that they could not be read, with the runtime's own words where it gave any. */
type Counted = { cancels: Cancels } | { unread: string | null };

/**
 * What the confirmation read as it opened: the agent archived already; or the agent as it is now (null where it could
 * not be read), what archiving it cancels, and whether its route lets Jason pause the provider's campaign first (null
 * where that was not read).
 */
type Read = { kind: 'archived' } | { kind: 'open'; record: Campaign | null; counted: Counted; usable: boolean | null };

/** What is said of a read that failed: the runtime's own words where it answered with an error, nothing otherwise. */
const reasonOf = (error: unknown): string | null => (error instanceof ApiError ? error.message : null);

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
    return countCancels(work, approvals, questions);
}

const pinOf = (campaign: Movable) => (campaign.external_ids ?? []).find((p) => p.kind === 'campaign');

/** Whether the agent's route for the provider's pause is usable; read only for a pinned agent that could pause first. */
async function readUsable(api: Api, record: Campaign): Promise<boolean | null> {
    if (!pinOf(record) || !jasonMayPauseFirst(record.status, true)) return null;
    const route = await api.call<RouteResolution>('route.resolve', {
        campaign_id: record.id,
        operation: 'campaign.pause',
    });
    return route.usable;
}

/** Reads the agent as it is now and, unless it is archived already, what archiving it cancels. */
async function readArchive(api: Api, id: string): Promise<Read> {
    let record: Campaign;
    try {
        record = await api.call<Campaign>('campaign.get', { campaign_id: id });
    } catch (error) {
        if (error instanceof Unpaired) throw error;
        return { kind: 'open', record: null, counted: { unread: reasonOf(error) }, usable: null };
    }
    if (record.status === 'archived') return { kind: 'archived' };
    const [cancels, usable] = await Promise.allSettled([readCancels(api, id), readUsable(api, record)]);
    for (const read of [cancels, usable])
        if (read.status === 'rejected' && read.reason instanceof Unpaired) throw read.reason;
    return {
        kind: 'open',
        record,
        counted: cancels.status === 'fulfilled' ? { cancels: cancels.value } : { unread: reasonOf(cancels.reason) },
        usable: usable.status === 'fulfilled' ? usable.value : null,
    };
}

const number = new Intl.NumberFormat('en-US');

/** What archiving cancels, from the counts read as the confirmation opened, or that they could not be read. */
function WhatArchivingCancels({ counted, onReadAgain }: { counted: Counted; onReadAgain: () => void }) {
    if ('unread' in counted)
        return (
            <section className="section archive-cancels" aria-label="What archiving cancels">
                <p>
                    What this agent has open could not be read
                    {counted.unread ? `. The runtime said: ${counted.unread}` : ': the runtime did not answer.'}
                </p>
                <p>
                    Archiving it still cancels its open work, the approvals on that work and its questions, and it
                    cannot be undone.
                </p>
                <div className="btn-row">
                    <button className="btn" type="button" onClick={onReadAgain}>
                        Read again
                    </button>
                </div>
            </section>
        );
    const { cancels } = counted;
    if (nothingOpen(cancels))
        return (
            <section className="section archive-cancels" aria-label="What archiving cancels">
                <p>Nothing is open: no work, no approval and no question is waiting.</p>
            </section>
        );
    const { work, processing, asked, given, questions, finishing } = cancels;
    const items = [
        work > 0 &&
            `${many(work, 'open work item', 'open work items')}${
                processing > 0
                    ? `, ${processing === 1 ? 'one of them' : `${number.format(processing)} of them`} processing now and stopped`
                    : ''
            }`,
        asked > 0 && many(asked, 'approval still asked of you', 'approvals still asked of you'),
        given > 0 && `${many(given, 'approval already given', 'approvals already given')}, whose work then never runs`,
        questions > 0 && many(questions, 'question waiting for an answer', 'questions waiting for an answer'),
    ].filter((each): each is string => Boolean(each));
    return (
        <section className="section archive-cancels" aria-label="What archiving cancels">
            {items.length > 0 && (
                <>
                    <p>Archiving cancels:</p>
                    <ul>
                        {items.map((each) => (
                            <li key={each}>{each}</li>
                        ))}
                    </ul>
                </>
            )}
            {finishing > 0 && (
                <p className="muted">
                    {many(finishing, 'search being stopped still finishes', 'searches being stopped still finish')}: the
                    archive leaves {finishing === 1 ? 'it' : 'them'} to run.
                </p>
            )}
        </section>
    );
}

/** What archiving leaves at the provider, and the way to stop it there first that is true for this agent. */
function TheProvidersCampaign({ name, pin, jasonFirst }: { name: string; pin: string; jasonFirst: boolean }) {
    return (
        <section className="section archive-provider" aria-label={`The campaign in ${name}`}>
            <p>
                The campaign in {name} (id {pin}) is left as it is: if it is live there, it keeps sending on its own
                schedule, and once this agent is archived Jason cannot pause it.
            </p>
            <p>
                {jasonFirst
                    ? `To stop it first, ask in this agent's chat to pause it in ${name}, and archive once that work has finished; or pause it in ${name} itself.`
                    : `To stop it first, pause it in ${name} itself, then archive.`}
            </p>
        </section>
    );
}

/** What the archive cancelled, as the confirmation counted it, or null where it could not: for the page's word after. */
export type WasOpen = Cancels | null;

/** The agent to archive: its id, name and status as any list carries them, and its provider's pins where they were read. */
type DialogProps = { campaign: Movable; onDone: (archived: Campaign, open: WasOpen) => void; onClose: () => void };

/** The confirmation: the agent named, that it cannot be undone, and what it cancels, read as it opens. */
export function ArchiveDialog({ campaign, onDone, onClose }: DialogProps) {
    const api = useApi();
    const chat = useCampaignChat() !== null;
    const [read, setRead] = useState<Read | null>(null);
    const [reading, setReading] = useState(0);
    const [reason, setReason] = useState('');
    const [busy, setBusy] = useState(false);
    const [refused, setRefused] = useState<string | null>(null);
    // The second of two guards against a second archive: a press that lands before React has drawn the button disabled,
    // the second click of a double click, finds the archive already under way and sends nothing.
    const sending = useRef(false);
    useEffect(() => {
        let current = true;
        setRead(null);
        readArchive(api, campaign.id).then(
            (answer) => {
                if (current) setRead(answer);
            },
            () => {
                // Only a lost pairing rejects: every other failure is a read that says so.
                if (current) session.clear();
            },
        );
        return () => {
            current = false;
        };
    }, [api, campaign.id, reading]);

    const act = async (open: WasOpen) => {
        if (sending.current) return;
        sending.current = true;
        setBusy(true);
        setRefused(null);
        try {
            onDone(await store.archive(api, campaign.id, reason.trim() || undefined), open);
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
            // A dialog of its own key, so it opens anew and, holding no field, takes the focus on Close: Escape reaches it.
            <Dialog key="archived" title={label} label={label} onClose={onClose} dismiss="Close" buttons={null}>
                <p>This agent is archived already, from another window or a terminal. Nothing more is done.</p>
            </Dialog>
        );
    // The provider's campaign is said from the agent as read now, or, where that read failed, from the agent the page
    // holds; Jason's own pause first is offered only where every fact it needs was read.
    const agent = read?.kind === 'open' ? (read.record ?? campaign) : campaign;
    const pin = pinOf(agent);
    const name = provider(agent);
    const jasonFirst =
        chat && read?.kind === 'open' && read.record !== null && jasonMayPauseFirst(read.record.status, read.usable);
    const open: WasOpen = read?.kind === 'open' && 'cancels' in read.counted ? read.counted.cancels : null;
    return (
        <Dialog
            title={label}
            label={label}
            onClose={onClose}
            buttons={
                <button
                    className="btn danger"
                    type="button"
                    // The first of two guards against a second archive: disabled from the press until the answer.
                    disabled={busy || read === null}
                    onClick={() => void act(open)}
                >
                    Archive
                </button>
            }
        >
            <p>An archive cannot be undone: nothing brings this agent back.</p>
            {read === null && <p className="muted">Reading what this agent has open…</p>}
            {read?.kind === 'open' && (
                <WhatArchivingCancels counted={read.counted} onReadAgain={() => setReading((n) => n + 1)} />
            )}
            {read !== null && pin && name && (
                <TheProvidersCampaign name={name} pin={pin.value} jasonFirst={jasonFirst} />
            )}
            <div className="field">
                <label htmlFor="archive-reason">Reason (optional, kept in the journal)</label>
                <ReasonField
                    id="archive-reason"
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

type SectionProps = { campaign: Campaign; onArchived: (archived: Campaign, open: WasOpen) => void };

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
                Archiving ends this agent for good and cannot be undone: its open work, the approvals on that work and
                its questions are cancelled.
            </p>
            <div className="btn-row">
                <button className="btn danger" type="button" onClick={() => setOpen(campaign.id)}>
                    Archive
                </button>
            </div>
            {open === campaign.id && (
                <ArchiveDialog
                    campaign={campaign}
                    onDone={(archived, was) => {
                        setOpen(null);
                        onArchived(archived, was);
                    }}
                    onClose={() => setOpen(null)}
                />
            )}
        </div>
    );
}
