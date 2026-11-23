import { signal } from '@preact/signals-react';
import { ApiError, Unpaired, type Api } from '../api/client';
import type { LaunchesLift, LaunchesList } from '../api/types';
import { t } from '../platform/i18n';
import { mode } from '../platform/mode';
import { repeated } from '../screens/attention/store';
import { clock as clockAt } from '../time/format';
import { act, define } from './actions';
import { html, type Html } from './html';
import { here } from './state';

// Needs you's one line for work held at a daily limit of launches: how many items wait, until the installation's day
// ends, and the one act that lets them go, the person's lift. It reads `launches.list` beside the lists of what waits and
// is no item of them: nothing here enters a count, the side column's, the filters', the dashboard's or the tray's. The
// demo draws no line, reads nothing and lifts nothing.

/** The reason the journal keeps for a lift made here. */
export const NEEDS_LIFT_REASON = 'Lifted from Needs you.';

/** The person's clock: the zone the console says every moment in. */
const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

/** What the last read of the limits said; null before one answered, and after one failed. */
export const limits = signal<LaunchesList | null>(null);

/** The lift made here, what it let go and until when, kept while Needs you stays in sight and nothing is held again. */
export const lifted = signal<LaunchesLift | null>(null);

/** Whether a lift is under way, from the press until the runtime answers. */
const lifting = signal(false);

/** What the runtime said when the last lift failed, in its own words, until the next press or a read with nothing held. */
const refused = signal<{ code: string | null; message: string } | null>(null);

/** Reads the limits again; a read that fails says nothing, and the line it would draw is not drawn. */
export async function readHeld(api: Api): Promise<void> {
    if (mode.value === 'demo') return;
    try {
        const read = await api.call<LaunchesList>('launches.list', {});
        limits.value = read;
        // Held again after a lift (the next day's limits), the line is the held line again.
        if (read.held > 0) lifted.value = null;
        if (read.held === 0) refused.value = null;
    } catch (error) {
        if (error instanceof Unpaired) return;
        limits.value = null;
    }
}

/** Needs you was shown anew: what a lift made here said is said no more, and the line is what the last read says. */
export function sightHeld(): void {
    lifted.value = null;
}

/** Forgets everything the line holds; for a test. */
export function forgetHeld(): void {
    limits.value = null;
    lifted.value = null;
    lifting.value = false;
    refused.value = null;
}

const until = (at: string): { until: string; zone: string } => ({ until: clockAt(new Date(at), zone), zone });

/** Whether the line is drawn: never in the demo; after a lift made here, or while a read says items are held. */
export function heldShown(): boolean {
    if (mode.value === 'demo') return false;
    return lifted.value !== null || (limits.value?.held ?? 0) > 0;
}

/** The line, or nothing: in the demo, before a read, and while nothing is held and no lift was made here. */
export function heldLine(): Html | null {
    if (mode.value === 'demo') return null;
    const done = lifted.value;
    if (done !== null) {
        const words = { ...until(done.until), count: String(done.released) };
        const said =
            done.released === 0
                ? t('needs.held.lifted.none', words)
                : done.released === 1
                  ? t('needs.held.lifted.one', words)
                  : t('needs.held.lifted.many', words);
        return html`<div class="note needs-held" role="status">${said}</div>`;
    }
    const read = limits.value;
    if (read === null || read.held <= 0) return null;
    const words = { ...until(read.day.end), count: String(read.held) };
    const failure = refused.value;
    return html`<div class="note needs-held" role="status">
        ${read.held === 1 ? t('needs.held.one', words) : t('needs.held.many', words)} ${t('needs.held.says')}
        <button class="btn sm" type="button" ${lifting.value ? html`disabled` : null} ${act('needs.lift')}>
            ${t('needs.held.lift')}
        </button>
        ${
            failure === null
                ? null
                : html`<div class="needs-held-failed" role="alert">
                      ${t('needs.held.failed')} ${failure.code === null ? null : html`<code>${failure.code}</code>`}
                      ${failure.message}
                  </div>`
        }
    </div>`;
}

// The lift is the person's word: the client names the paired person as its actor, as it does on an Approve here.
define('needs.lift', (_, __, event) => {
    if (repeated(event) || lifting.value || mode.value === 'demo') return;
    const api = here().api;
    lifting.value = true;
    refused.value = null;
    api.call<LaunchesLift>('launches.lift', { reason: NEEDS_LIFT_REASON }, 'write')
        .then((answer) => {
            lifted.value = answer;
            if (limits.value !== null) limits.value = { ...limits.value, held: 0 };
        })
        .catch((error: unknown) => {
            if (error instanceof Unpaired) return;
            refused.value =
                error instanceof ApiError
                    ? { code: error.code, message: error.message }
                    : { code: null, message: error instanceof Error ? error.message : String(error) };
        })
        .finally(() => {
            lifting.value = false;
        });
});
