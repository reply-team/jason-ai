import { signal } from '@preact/signals-react';
import { t } from '../platform/i18n';
import { act, define } from './actions';
import { html, type Html } from './html';
import { svg } from './icons';
import { markdown, type Block, type Inline } from './markdown';

// The agent's words drawn from the Markdown subset's tree. Every word is text the html tag escapes; the elements are
// the ones named here and no others, and a link is a span of text, never an anchor.

/** The block just pressed and what came of it, while its button says so; no other block says it. */
export const copied = signal<{ key: string; outcome: 'copied' | 'refused' } | null>(null);
let forget: ReturnType<typeof setTimeout> | undefined;

function say(key: string, outcome: 'copied' | 'refused'): void {
    copied.value = { key, outcome };
    clearTimeout(forget);
    forget = setTimeout(() => {
        copied.value = null;
    }, 2000);
}

/** Puts a code block's text on the clipboard as it is: the way Copy prompt does, by the page's own clipboard. */
define('chat.copyCode', (args, element) => {
    const key = typeof args[0] === 'string' ? args[0] : '';
    const text = element.closest('.chat-md-code')?.querySelector('code')?.textContent;
    if (text === undefined || text === null) return;
    if (typeof navigator.clipboard?.writeText !== 'function') {
        say(key, 'refused');
        return;
    }
    void navigator.clipboard.writeText(text).then(
        () => say(key, 'copied'),
        () => say(key, 'refused'),
    );
});

function inlineView(item: Inline): Html {
    switch (item.kind) {
        case 'text':
            return html`${item.text}`;
        case 'em':
            return html`<em>${item.children.map(inlineView)}</em>`;
        case 'strong':
            return html`<strong>${item.children.map(inlineView)}</strong>`;
        case 'code':
            return html`<code>${item.text}</code>`;
        case 'link':
            return item.text === '' || item.text === item.href
                ? html`<span class="chat-md-link">${item.href}</span>`
                : html`<span class="chat-md-link">${item.text} (${item.href})</span>`;
    }
}

const inlines = (items: readonly Inline[]): Html[] => items.map(inlineView);

function heading(level: number, children: Html[]): Html {
    switch (level) {
        case 1:
            return html`<h1>${children}</h1>`;
        case 2:
            return html`<h2>${children}</h2>`;
        case 3:
            return html`<h3>${children}</h3>`;
        case 4:
            return html`<h4>${children}</h4>`;
        case 5:
            return html`<h5>${children}</h5>`;
        default:
            return html`<h6>${children}</h6>`;
    }
}

let place = '';
let counter = 0;

function blockView(block: Block): Html {
    switch (block.kind) {
        case 'paragraph':
            return html`<p>${inlines(block.children)}</p>`;
        case 'heading':
            return heading(block.level, inlines(block.children));
        case 'code': {
            const key = `${place}:${counter++}`;
            const said = copied.value?.key === key ? copied.value.outcome : null;
            // An icon, named by the words it stands for, which a pointer that rests on it shows too.
            const [label, mark] =
                said === 'copied'
                    ? [t('chat.code.copied'), 'copied' as const]
                    : said === 'refused'
                      ? [t('chat.code.notCopied'), 'notCopied' as const]
                      : [t('chat.code.copy'), 'copy' as const];
            return html`<div class="chat-md-code">
                <button
                    type="button"
                    class="chat-md-copy"
                    aria-label="${label}"
                    title="${label}"
                    ${act('chat.copyCode', key)}
                >
                    ${svg(mark, 16)}
                </button>
                <pre><code>${block.text}</code></pre>
            </div>`;
        }
        case 'list': {
            const items = block.items.map((item) => html`<li>${item.map(blockView)}</li>`);
            return block.ordered
                ? html`<ol start="${block.start}">
                      ${items}
                  </ol>`
                : html`<ul>
                      ${items}
                  </ul>`;
        }
        case 'quote':
            return html`<blockquote>${block.children.map(blockView)}</blockquote>`;
        case 'table':
            return html`<div class="chat-md-table">
                <table>
                    <thead>
                        <tr>
                            ${block.head.map((cell) => html`<th>${inlines(cell)}</th>`)}
                        </tr>
                    </thead>
                    <tbody>
                        ${block.rows.map(
                            (row) =>
                                html`<tr>
                                    ${row.map((cell) => html`<td>${inlines(cell)}</td>`)}
                                </tr>`,
                        )}
                    </tbody>
                </table>
            </div>`;
    }
}

/** The agent's text, drawn as the Markdown subset. */
export function markdownView(source: string, where = ''): Html {
    place = where;
    counter = 0;
    return html`<div class="chat-md">${markdown(source).map(blockView)}</div>`;
}
