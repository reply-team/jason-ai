import { html, type Html } from './html';
import { toActivity } from './live';
import { clock, lines, streamState } from './state';

// Activity: the journal as it is written, line by line, through the runtime's stream.

const statusWords = {
    connecting: 'Connecting to the journal…',
    open: 'Following the journal',
    waiting: 'The runtime is not answering; trying again',
    stopped: 'Stopped',
} as const;

export function activityView(): Html {
    const state = streamState.value;
    const now = clock.value;
    return html`<div class="panel-body">
        <div class="ph">
            <div>
                <h1>Activity</h1>
                <p>What Jason and the AI do, as the journal records it.</p>
            </div>
        </div>
        <div class="meta stream-${state.status}" role="status">${statusWords[state.status]}</div>
        ${
            state.fresh
                ? html`<div class="note">
                      The runtime no longer holds the last line this window saw, so lines written in between are not
                      shown.
                  </div>`
                : null
        }
        ${
            lines.value.length === 0
                ? html`<div class="empty">Nothing has been recorded since this window opened.</div>`
                : html`<div class="tl">
                      ${lines.value.map((line) => {
                          const row = toActivity(line, now);
                          return html`<div class="ev activity" data-id="${row.id}">
                              <span class="meta">${row.age}</span>
                              <b>${row.what}</b>
                              ${row.campaign ? html`<span>${row.campaign}</span>` : null}
                              ${row.contact ? html`<span>${row.contact}</span>` : null}
                              <span class="meta">${row.who}</span>
                          </div>`;
                      })}
                  </div>`
        }
    </div>`;
}
