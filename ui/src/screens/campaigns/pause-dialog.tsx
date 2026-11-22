import { useEffect, useState } from 'react';
import { ApiError, Unpaired, type Api } from '../../api/client';
import { useApi } from '../../api/context';
import { session } from '../../api/session';
import type { Campaign, CampaignSummary, CampaignSummaryResponse, ExternalId, ModeInForce } from '../../api/types';
import { t } from '../../platform/i18n';
import { sourceWords } from '../send-modes/agent-modes';
import { kinds, modeWords, type Kind } from '../send-modes/model';
import * as store from './store';
import { Dialog } from '../../layout/dialog';
import { Notice } from '../../layout/notice';
import { ReasonField } from '../../layout/reason-field';
import { verbs, type Transition } from './moves-model';

export { transitions, verbs, type Transition } from './moves-model';

/**
 * The campaign a move is offered for: its id, name and status, as every list of campaigns carries them, and the
 * provider's pins where the caller has read them. Without the pins, the Pause dialog reads the campaign before it says
 * what pausing does.
 */
export type Movable = CampaignSummary & { external_ids?: ExternalId[]; context?: Record<string, unknown> };

type Props = { campaign: Movable; kind: Transition; onDone: () => void; onClose: () => void };

/** The provider campaign's own name for itself: the plugin that holds its pin, as a person would write it. */
export function provider(campaign: Movable): string | null {
    const pin = (campaign.external_ids ?? []).find((p) => p.kind === 'campaign');
    return pin ? pin.plugin_id.charAt(0).toUpperCase() + pin.plugin_id.slice(1) : null;
}

/** What pausing or starting does, said before it is done: Jason's own release of work, and nothing at the provider. */
function says(campaign: Movable, kind: Transition): string {
    const name = provider(campaign);
    // Starting or resuming releases the work queued for the provider; a sequence already live there sends regardless.
    const own = (verb: string) =>
        name
            ? ` Work waiting here for ${name}, such as starting its sequence or adding people to it, runs once you ${verb} this agent. ${name}'s sequence, once live, sends on its own schedule whether this agent runs or not.`
            : '';
    if (kind === 'resume') return `Jason releases this agent's work again.${own('resume')}`;
    if (kind === 'start') return `Jason starts releasing this agent's work.${own('start')}`;
    const sequence = name ? ` ${name}'s sequence keeps sending until a role pauses it there.` : '';
    return `Jason stops releasing this agent's work. Work already running finishes.${sequence} Your reason is recorded.`;
}

/**
 * What starting or resuming lets go, as the runtime answers when the dialog opens: the people the campaign holds, and the
 * send mode each kind of message goes under now. Each is null where its read failed; `modes` is null too from a runtime
 * that does not say its modes.
 */
type Releases = { people: number | null; modes: Record<Kind, ModeInForce> | null; record: Campaign | null };

const number = new Intl.NumberFormat('en-US');

/** "22 people are in this agent.": the count the agent's page shows, as a sentence. */
function peopleWords(people: number): string {
    if (people === 0) return 'No one is in this agent yet.';
    return people === 1 ? '1 person is in this agent.' : `${number.format(people)} people are in this agent.`;
}

/** Reads what starting the campaign lets go: its own record for the modes in force, and its numbers for the people. */
async function readReleases(api: Api, id: string): Promise<Releases> {
    const [record, numbers] = await Promise.allSettled([
        api.call<Campaign>('campaign.get', { campaign_id: id }),
        api.call<CampaignSummaryResponse>('campaign.summary', { campaign_ids: [id], ...store.archivedLeftOut }),
    ]);
    for (const read of [record, numbers])
        if (read.status === 'rejected' && read.reason instanceof Unpaired) throw read.reason;
    const row =
        numbers.status === 'fulfilled' ? numbers.value.campaigns.find((each) => each.campaign_id === id) : undefined;
    return {
        people: row && typeof row.people === 'number' ? row.people : null,
        modes: record.status === 'fulfilled' ? (record.value.send_modes_in_force ?? null) : null,
        record: record.status === 'fulfilled' ? record.value : null,
    };
}

/**
 * Before work is released: how many people the agent holds, and how its messages go now, in Settings' own words for each
 * kind of person — every message waiting for the person, or going automatically, and where that comes from. Text only.
 */
function WhatStartingReleases({ releases }: { releases: Releases }) {
    const { people, modes } = releases;
    return (
        <section className="section start-releases" aria-label="What starting releases">
            {people !== null && <p>{peopleWords(people)}</p>}
            <h3>{t('modes.agent.heading')}</h3>
            {modes === null ? (
                <p className="muted">{t('modes.settings.unreadable')}</p>
            ) : (
                <>
                    {kinds.map((each) => (
                        <p key={each}>
                            <strong>{t(`modes.kind.${each}`)}</strong>: {modeWords(modes[each].mode).name} ·{' '}
                            <span className="muted">{sourceWords(modes[each].source)}</span>
                        </p>
                    ))}
                    {kinds.some((each) => modes[each].mode === 'automatic') && (
                        <p className="muted">{t('modes.automatic.note')}</p>
                    )}
                </>
            )}
        </section>
    );
}

/** Pause, Resume or Start, with an optional reason the journal keeps. */
export function TransitionDialog({ campaign, kind, onDone, onClose }: Props) {
    const api = useApi();
    const [reason, setReason] = useState('');
    const [busy, setBusy] = useState(false);
    const [refused, setRefused] = useState<string | null>(null);
    const verb = verbs[kind];
    // Starting and resuming release the same queued work, so both say first what goes, read as the dialog opens.
    const releasing = kind !== 'pause';
    const [releases, setReleases] = useState<Releases | null>(null);
    useEffect(() => {
        if (!releasing) return;
        let current = true;
        setReleases(null);
        readReleases(api, campaign.id).then(
            (read) => {
                if (current) setReleases(read);
            },
            () => {
                if (current) session.clear();
            },
        );
        return () => {
            current = false;
        };
    }, [api, campaign.id, releasing]);
    // A Pause offered where the provider's pins were not read reads the campaign first, so it can say what keeps sending.
    const readsRecord = kind === 'pause' && campaign.external_ids === undefined;
    const [record, setRecord] = useState<Campaign | null | undefined>(undefined);
    useEffect(() => {
        if (!readsRecord) return;
        let current = true;
        setRecord(undefined);
        api.call<Campaign>('campaign.get', { campaign_id: campaign.id }).then(
            (read) => {
                if (current) setRecord(read);
            },
            (error: unknown) => {
                if (!current) return;
                if (error instanceof Unpaired) session.clear();
                else setRecord(null);
            },
        );
        return () => {
            current = false;
        };
    }, [api, campaign.id, readsRecord]);
    const reading = (releasing && releases === null) || (readsRecord && record === undefined);

    const act = async () => {
        setBusy(true);
        setRefused(null);
        try {
            await store.transition(
                api,
                kind === 'pause' ? 'campaign.pause' : 'campaign.start',
                campaign.id,
                reason.trim() || undefined,
            );
            onDone();
        } catch (error) {
            if (error instanceof Unpaired) session.clear();
            else setRefused(error instanceof ApiError ? error.message : String(error));
        } finally {
            setBusy(false);
        }
    };

    return (
        <Dialog
            title={
                <>
                    {verb} {campaign.name}
                </>
            }
            label={`${verb} ${campaign.name}`}
            onClose={onClose}
            buttons={
                <>
                    <button className="btn primary" type="button" disabled={busy || reading} onClick={() => void act()}>
                        {verb}
                    </button>
                </>
            }
        >
            <p>{says(releases?.record ?? record ?? campaign, kind)}</p>
            {reading && <p className="muted">{t('modes.settings.reading')}</p>}
            {releases && <WhatStartingReleases releases={releases} />}
            <div className="field">
                <label htmlFor="transition-reason">Reason (optional, kept in the journal)</label>
                <ReasonField
                    id="transition-reason"
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
