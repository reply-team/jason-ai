import { signal } from '@preact/signals-react';
import type { CampaignActivityResponse } from '../api/types';
import { t } from '../platform/i18n';
import { mode } from '../platform/mode';
import { archivedLeftOut } from '../screens/campaigns/store';
import { act, argsOf, define } from './actions';
import * as campaigns from './campaigns';
import {
    categoryLabel,
    cut,
    dashboardPeriods,
    dashboardWindow,
    defaultDashboardPeriod,
    greetingFor,
    isDashboardPeriod,
    periodWords,
    progressOf,
    startingRange,
    tilesOf,
    type ActivityRead,
    type CustomRange,
    type DashboardPeriod,
    type ReplyCountRead,
} from './dashboard-model';
import { toFeedLine, toFeedRow, type FeedRow } from './feed';
import { html, type Html } from './html';
import { svg } from './icons';
import { toActivity, toApprovalRow, toCampaignRow, toQuestionRow } from './live';
import * as needs from './needs';
import { clock, here, lines } from './state';

// The Dashboard: where things stand, drawn only from reads the console already makes. Needs you and the Agents panel
// are read by their own modules; its own reads are the period's counts, asked the way the Performance screen asks
// them, and the replies a role judged, by category. One period holds for all of them and is not remembered. A number
// no read gives is a dash, never a zero.

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

/** The period every tile and card counts over; the Dashboard opens on thirty days each time. */
export const period = signal<DashboardPeriod>(defaultDashboardPeriod);
/** The days of a custom period; only read while the period is Custom. */
export const custom = signal<CustomRange>({ from: '', to: '' });

/** What `reply.count` answered for the period; null until read, while no window is asked for, or when the read failed. */
export const replies = signal<ReplyCountRead | null>(null);

/** The period's counts across the agents not archived, and by agent; null until read or when the read failed. */
export const activity = signal<{ total: ActivityRead; byCampaign: Record<string, ActivityRead> } | null>(null);

let generation = 0;
/** How many Dashboard panels are open: the journal's reload reads the counts again only for one that is. */
let open = 0;

/** The period's counts and replies; a late answer is dropped, and a failed read leaves its numbers unknown. */
export async function reload(): Promise<void> {
    const mine = ++generation;
    const window = dashboardWindow(period.value, new Date(), zone, custom.value);
    if (window === null) {
        activity.value = null;
        replies.value = null;
        return;
    }
    const counts = async () => {
        try {
            const read = await here().api.call<CampaignActivityResponse>('campaign.activity', {
                ...archivedLeftOut,
                ...window,
            });
            if (mine !== generation) return;
            activity.value = {
                total: { contacted: read.total.contacted, replied: read.total.replied },
                byCampaign: Object.fromEntries(
                    read.campaigns.map((row) => [row.campaign_id, { contacted: row.contacted, replied: row.replied }]),
                ),
            };
        } catch {
            if (mine === generation) activity.value = null;
        }
    };
    const judged = async () => {
        try {
            const read = await here().api.call<ReplyCountRead>('reply.count', window);
            if (mine === generation) replies.value = read;
        } catch {
            if (mine === generation) replies.value = null;
        }
    };
    await Promise.all([counts(), judged()]);
}

/** Called by the panel as it opens and closes. */
export function watching(on: boolean): void {
    open = Math.max(0, open + (on ? 1 : -1));
    if (on) {
        period.value = defaultDashboardPeriod;
        void reload();
    }
}

/** The journal said something changed: the counts are read again when a Dashboard is open. */
export function reloadIfOpen(): void {
    if (open > 0) void reload();
}

define('dash.period', (_args, element) => {
    if (!(element instanceof HTMLSelectElement) || !isDashboardPeriod(element.value)) return;
    if (element.value === 'custom' && !custom.value.from) custom.value = startingRange(new Date(), zone);
    period.value = element.value;
    void reload();
});
define('dash.range', ([end], element) => {
    if ((end !== 'from' && end !== 'to') || !(element instanceof HTMLInputElement)) return;
    custom.value = { ...custom.value, [end]: element.value };
    void reload();
});
define('dash.new', () => here().dock.show('start'));
define('dash.needs', () => here().dock.show('needs'));
define('dash.agents', () => here().dock.show('campaigns'));
define('dash.item', ([kind, id]) => {
    if ((kind !== 'approvals' && kind !== 'questions') || typeof id !== 'string') return;
    here().dock.show('needs');
    void needs.attention.open(here().api, kind, id);
});

const dash = html`${t('dash.dash')}`;

const maxApprovals = 4;
const maxQuestions = 2;
const maxLines = 6;

/** The Feed's rows from the journal's tail the console keeps: the kinds the Feed takes, in the Feed's words. */
function recent(now: number): { row: FeedRow; who: string }[] {
    const names = new Map(campaigns.campaigns.value.map((each) => [each.id, each.name]));
    const found: { row: FeedRow; who: string }[] = [];
    for (const line of lines.value) {
        if (found.length === maxLines) break;
        const handed = toFeedLine(line);
        if (handed === null) continue;
        const row = toFeedRow(
            {
                kind: 'line',
                journal_kind: handed.kind,
                journal_id: handed.id,
                at: handed.ts,
                campaign_id: handed.campaign_id,
                count: 1,
                ...(handed.work_item_operation ? { operation: handed.work_item_operation } : {}),
                ...(handed.work_item_role ? { role: handed.work_item_role } : {}),
            },
            names,
            now,
        );
        if (row !== null) found.push({ row, who: toActivity(line, now).who });
    }
    return found;
}

function periodControl(): Html {
    return html`<span class="dash-period-pick">
        <select class="inp" aria-label="${t('dash.period')}" data-change="dash.period">
            ${dashboardPeriods.map(
                (each) =>
                    html`<option value="${each}" ${period.value === each ? html`selected` : null}>
                        ${t(`dash.period.${each}`)}
                    </option>`,
            )}
        </select>
        ${
            period.value === 'custom'
                ? html`<input
                          class="inp"
                          type="date"
                          aria-label="${t('dash.period.from')}"
                          value="${custom.value.from}"
                          data-change="dash.range"
                          ${argsOf('from')}
                      /><input
                          class="inp"
                          type="date"
                          aria-label="${t('dash.period.to')}"
                          value="${custom.value.to}"
                          data-change="dash.range"
                          ${argsOf('to')}
                      />`
                : null
        }
    </span>`;
}

function header(waiting: number): Html {
    // The installation knows no person's name, so the greeting has none and no comma.
    const hour = new Date(clock.value).getHours();
    return html`<div class="ph dash-head">
        <div>
            <h1>${greetingFor(hour)}</h1>
            <p>${mode.value === 'demo' ? t('dash.sub.demo') : t('dash.sub.live')}</p>
        </div>
        <div class="acts">
            ${periodControl()}
            ${
                waiting > 0
                    ? html`<button class="btn p" type="button" ${act('dash.needs')}>
                          ${t('dash.review', { n: String(waiting) })}
                      </button>`
                    : null
            }
            <button class="btn ${waiting > 0 ? '' : 'p'}" type="button" ${act('dash.new')}>
                ${svg('plus', 14)}${t('dash.new')}
            </button>
        </div>
    </div>`;
}

function tiles(): Html {
    const read = activity.value;
    const list = tilesOf({
        approvals: needs.attention.approvals.value.length,
        questions: needs.attention.questions.value.length,
        waitingKnown: needs.attention.loaded.value,
        activity: read === null ? null : read.total,
        replies: replies.value,
    });
    return html`<div class="dash-tiles" role="group" aria-label="${t('dash.tiles')}">
        ${list.map(
            (tile) =>
                html`<div
                    class="dash-tile"
                    data-tile="${tile.label}"
                    ${tile.hint === undefined ? null : html`title="${tile.hint}"`}
                >
                    <span class="label">${tile.label}</span>
                    ${
                        tile.value === null
                            ? html`<b class="dash-none" aria-label="${t('dash.tile.none')}">${t('dash.dash')}</b
                                  ><span class="rel">${t('dash.tile.none')}</span>`
                            : html`<b>${tile.value}</b
                                  >${tile.sub === null ? null : html`<span class="rel">${tile.sub}</span>`}`
                    }
                </div>`,
        )}
    </div>`;
}

function decide(now: number): Html {
    const approvals = needs.attention.approvals.value;
    const questions = needs.attention.questions.value;
    const all = approvals.length + questions.length;
    return html`<section class="card dash-card dash-decide">
        <div class="hd">
            <h3>${t('dash.decide')}</h3>
            ${all > 0 ? html`<button class="btn sm" type="button" ${act('dash.needs')}>${t('dash.decide.all', { n: String(all) })}</button>` : null}
        </div>
        <div class="bd">
            ${
                !needs.attention.loaded.value
                    ? html`<div class="empty">${t('dash.decide.reading')}</div>`
                    : all === 0
                      ? html`<div class="empty dash-none-waiting">${t('dash.decide.none')}</div>`
                      : html`${approvals.slice(0, maxApprovals).map((each) => {
                            const row = toApprovalRow(each, now);
                            return html`<button
                                class="dash-row"
                                type="button"
                                data-id="${row.id}"
                                ${act('dash.item', 'approvals', row.id)}
                            >
                                <span
                                    ><b
                                        >${[row.what, row.who, row.campaign].filter((part) => part).join(' · ')}</b
                                    ></span
                                >
                                <span class="meta">${row.age}</span>
                            </button>`;
                        })}
                        ${questions.slice(0, maxQuestions).map((each) => {
                            const row = toQuestionRow(each, now);
                            return html`<button
                                class="dash-row"
                                type="button"
                                data-id="${row.id}"
                                ${act('dash.item', 'questions', row.id)}
                            >
                                <span
                                    ><b>${cut(row.question, 90)}</b>
                                    <span class="meta"
                                        >${t('dash.decide.question', { agent: row.campaign })}</span
                                    ></span
                                >
                                <span class="meta">${row.age}</span>
                            </button>`;
                        })}`
            }
        </div>
    </section>`;
}

function liveAgents(): Html {
    const read = activity.value;
    const running = campaigns.campaigns.value.filter((each) => each.status === 'active');
    return html`<section class="card dash-card dash-agents">
        <div class="hd">
            <h3>${t('dash.agents')}</h3>
            <span class="meta dash-period">${t('dash.agents.period', { period: periodWords(period.value) })}</span>
            <button class="btn sm" type="button" ${act('dash.agents')}>${t('dash.agents.all')}</button>
        </div>
        <div class="bd">
            ${
                running.length === 0
                    ? html`<div class="empty dash-no-agents">${t('dash.agents.none')}</div>`
                    : html`<table class="t">
                          <thead>
                              <tr>
                                  <th>${t('dash.col.agent')}</th>
                                  <th>${t('dash.col.status')}</th>
                                  <th>${t('dash.col.people')}</th>
                                  <th>${t('dash.col.contacted')}</th>
                                  <th>${t('dash.col.replied')}</th>
                                  <th>${t('dash.col.meetings')}</th>
                                  <th>${t('dash.col.progress')}</th>
                              </tr>
                          </thead>
                          <tbody>
                              ${running.map((campaign) => {
                                  const counts = campaigns.summary.value[campaign.id];
                                  const row = toCampaignRow(campaign, counts);
                                  const mine = read?.byCampaign[campaign.id];
                                  const progress = mine ? progressOf(mine.contacted, row.people) : null;
                                  return html`<tr data-id="${row.id}">
                                      <td>
                                          <button class="dash-link" type="button" ${act('campaigns.open', row.id)}>
                                              <b>${row.name}</b>
                                          </button>
                                          ${
                                              counts?.sequence_linked && counts.sequence > 0
                                                  ? html`<div class="meta">
                                                        ${t('dash.agents.sequence', { n: String(counts.sequence) })}
                                                    </div>`
                                                  : null
                                          }
                                      </td>
                                      <td><span class="pillc ${row.tone}">${row.state}</span></td>
                                      <td>${row.people}</td>
                                      <td>${mine ? mine.contacted : dash}</td>
                                      <td>${mine ? mine.replied : dash}</td>
                                      <td>${dash}</td>
                                      <td>
                                          ${
                                              progress === null
                                                  ? dash
                                                  : html`<progress
                                                        value="${progress}"
                                                        max="1"
                                                        aria-label="${t('dash.progress', { period: periodWords(period.value), agent: row.name })}"
                                                    ></progress>`
                                          }
                                      </td>
                                  </tr>`;
                              })}
                          </tbody>
                      </table>`
            }
        </div>
    </section>`;
}

function categories(): Html {
    const read = replies.value;
    return html`<section class="card dash-card dash-categories">
        <div class="hd">
            <h3>${t('dash.cats')}</h3>
            <span class="meta dash-period">${periodWords(period.value)}</span>
        </div>
        <div class="bd">
            ${
                read === null
                    ? html`<div class="empty dash-cats-none">${t('dash.cats.unread')}</div>`
                    : read.total.categories.length === 0
                      ? html`<div class="empty dash-cats-none">${t('dash.cats.none')}</div>`
                      : html`<table class="t">
                            <thead>
                                <tr>
                                    <th>${t('dash.cats.col.category')}</th>
                                    <th>${t('dash.cats.col.threads')}</th>
                                    <th>${t('dash.cats.col.replies')}</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${read.total.categories.map(
                                    (each) =>
                                        html`<tr data-category="${each.category}">
                                            <td>${categoryLabel(each.category)}</td>
                                            <td>${each.threads}</td>
                                            <td>${each.replies}</td>
                                        </tr>`,
                                )}
                            </tbody>
                        </table>`
            }
        </div>
    </section>`;
}

function recentActivity(now: number): Html {
    const rows = recent(now);
    return html`<section class="card dash-card dash-recent">
        <div class="hd"><h3>${t('dash.activity')}</h3></div>
        <div class="bd">
            ${
                rows.length === 0
                    ? html`<div class="empty">${t('dash.activity.none')}</div>`
                    : rows.map(
                          ({ row, who }) =>
                              html`<div class="dash-line" data-id="${row.id}">
                                  <div>
                                      <b>${row.words}</b>
                                      <button class="dash-link" type="button" ${act('feed.campaign', row.campaignId)}>
                                          ${row.campaign}
                                      </button>
                                  </div>
                                  <div class="meta">${row.age} · ${who}</div>
                              </div>`,
                      )
            }
        </div>
    </section>`;
}

export function dashboardView(): Html {
    const now = clock.value;
    const waiting = needs.attention.approvals.value.length + needs.attention.questions.value.length;
    if (!campaigns.read.value)
        return html`<div class="panel-body dash">
            <div class="ph">
                <div><h1>${t('dash.title')}</h1></div>
            </div>
            <div class="empty">${t('dash.reading')}</div>
        </div>`;
    if (campaigns.campaigns.value.length === 0)
        return html`<div class="panel-body dash">
            <div class="ph">
                <div>
                    <h1>${t('dash.title')}</h1>
                    <p>${t('dash.empty.sub')}</p>
                </div>
            </div>
            <div class="card dash-empty">
                <div class="hd"><h3>${t('dash.empty.title')}</h3></div>
                <div class="bd">
                    <p>${t('dash.empty.body')}</p>
                    <button class="btn p" type="button" ${act('dash.new')}>${svg('plus', 14)}${t('dash.new')}</button>
                </div>
            </div>
        </div>`;
    return html`<div class="panel-body dash">
        ${header(waiting)} ${tiles()}
        <div class="dash-grid">${decide(now)} ${liveAgents()}</div>
        ${categories()} ${recentActivity(now)}
    </div>`;
}
