import { signal } from '@preact/signals-react';
import { act, argsOf, define } from './actions';
import { campaigns } from './campaigns';
import { html, type Html } from './html';
import { svg } from './icons';
import { attention } from './needs';
import { pageSize, recentFilters, recentRows, shownRows, type RecentFilter, type RecentRow } from './recent-rows';
import { chats, clock, conversations, currentChat } from './state';

// "Recent" in the sidebar: the conversations, each with a dot, its title, what waits on the person and its age, a search
// box and a filter in the header, ten rows at a time. Everything here is read from what the console already holds; the
// state below is the page's own and is kept nowhere.

/** Whether the search box is drawn, and what is typed in it. */
const searching = signal(false);
const text = signal('');
/** The filter entry in force, whether its menu is open, and how many rows "Load more" has uncovered. */
const filter = signal<RecentFilter>('all');
const menuOpen = signal(false);
const limit = signal(pageSize);

/** Puts the search, the filter and the paging back as a fresh window has them. */
export function resetRecent(): void {
    searching.value = false;
    text.value = '';
    filter.value = 'all';
    menuOpen.value = false;
    limit.value = pageSize;
}

/** Closes the filter's menu. */
export function closeFilterMenu(): void {
    menuOpen.value = false;
}

const labels: Readonly<Record<RecentFilter, string>> = {
    all: 'All',
    needs: 'Needs you',
    live: 'Live',
    paused: 'Paused',
    draft: 'Draft',
};

const dotTitles = {
    waiting: 'Waiting on you',
    active: 'Active',
    paused: 'Paused',
    draft: 'Draft',
    none: 'No agent',
} as const;

define('recent.search', (_args, element) => {
    searching.value = !searching.value;
    if (!searching.value) {
        text.value = '';
        return;
    }
    closeFilterMenu();
    element.closest('.side')?.querySelector<HTMLInputElement>('[data-input="recent.type"]')?.focus();
});
define('recent.type', (_args, element) => {
    if (element instanceof HTMLInputElement) text.value = element.value;
});
define('recent.close', () => {
    searching.value = false;
    text.value = '';
});
define('recent.menu', () => {
    menuOpen.value = !menuOpen.value;
});
define('recent.filter', ([value]) => {
    const chosen = recentFilters.find((each) => each === value);
    if (!chosen) return;
    filter.value = chosen;
    limit.value = pageSize;
    closeFilterMenu();
});
define('recent.more', () => {
    limit.value += pageSize;
});

function rowView(row: RecentRow): Html {
    const said = [
        row.waiting > 0 ? `${row.waiting} waiting` : null,
        row.age === '' ? null : row.age,
        row.agent === null ? null : dotTitles[row.dot],
    ]
        .filter((each) => each !== null)
        .join(' · ');
    return html`<button
        class="item recent-row"
        type="button"
        aria-label="${row.title}"
        aria-description="${said}"
        ${row.id === currentChat.value ? html`aria-current="true"` : null}
        ${act('side.chat', row.id)}
    >
        <span
            class="rdot ${row.dot}${row.running ? ' running' : ''}"
            title="${dotTitles[row.dot]}"
            aria-hidden="true"
        ></span>
        <span class="rt">${row.title}</span>
        ${row.waiting > 0 ? html`<span class="count" aria-hidden="true">${row.waiting}</span>` : null}
        <span class="t" aria-hidden="true">${row.age}</span>
    </button>`;
}

function filterMenu(): Html {
    return html`<button class="menu-away" type="button" tabindex="-1" aria-hidden="true" ${act('recent.menu')}></button>
        <div class="menu" role="menu" aria-label="Show">
            <div class="mhead">Show</div>
            ${recentFilters.map(
                (each) =>
                    html`<button
                        class="menuitem"
                        type="button"
                        role="menuitemradio"
                        aria-checked="${filter.value === each ? 'true' : 'false'}"
                        ${act('recent.filter', each)}
                    >
                        ${filter.value === each ? svg('check', 15) : html`<span></span>`}<span class="mt"
                            >${labels[each]}</span
                        >
                    </button>`,
            )}
        </div>`;
}

/** The header, the search box when it is open, and the list of conversations. */
export function recentView(): Html {
    const rows = recentRows({
        conversations: conversations.value,
        titles: (id) => chats.value[id]?.title,
        running: (id) => chats.value[id]?.running,
        campaigns: campaigns.value,
        approvals: attention.approvals.value,
        questions: attention.questions.value,
        now: clock.value,
    });
    const { rows: shown, more } = shownRows(rows, filter.value, text.value, limit.value);
    return html`<div class="recent">
            <span class="rl">Recent</span>
            <span class="rtools">
                <button
                    class="rbtn"
                    type="button"
                    title="Search agents"
                    aria-label="Search agents"
                    aria-pressed="${searching.value ? 'true' : 'false'}"
                    ${act('recent.search')}
                >
                    ${svg('search', 14)}
                </button>
                <button
                    class="rbtn"
                    type="button"
                    title="Filter"
                    aria-label="Filter"
                    aria-haspopup="menu"
                    aria-expanded="${menuOpen.value ? 'true' : 'false'}"
                    data-active="${filter.value === 'all' ? 'false' : 'true'}"
                    ${act('recent.menu')}
                >
                    ${svg('filter', 14)}
                </button>
            </span>
            ${menuOpen.value ? filterMenu() : null}
        </div>
        ${
            searching.value
                ? html`<div class="rsearch">
                      <input
                          class="inp"
                          type="text"
                          aria-label="Search agents"
                          placeholder="Search agents"
                          autocomplete="off"
                          value="${text.value}"
                          data-input="recent.type"
                          data-escape="recent.close"
                          ${argsOf()}
                      />
                  </div>`
                : null
        }
        <nav class="list" aria-label="Conversations">
            ${shown.map(rowView)}
            ${
                more
                    ? html`<button class="item rmore" type="button" ${act('recent.more')}>
                          <span>Load more</span>
                      </button>`
                    : null
            }
        </nav>
        ${
            shown.length > 0
                ? null
                : html`<div class="rnone">${rows.length === 0 ? 'No chats yet.' : 'No agents match.'}</div>`
        }`;
}
