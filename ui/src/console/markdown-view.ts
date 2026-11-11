import { html, type Html } from './html';
import { markdown, type Block, type Inline } from './markdown';

// The agent's words drawn from the Markdown subset's tree. Every word is text the html tag escapes; the elements are
// the ones named here and no others, and a link is a span of text, never an anchor.

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

function blockView(block: Block): Html {
    switch (block.kind) {
        case 'paragraph':
            return html`<p>${inlines(block.children)}</p>`;
        case 'heading':
            return heading(block.level, inlines(block.children));
        case 'code':
            return html`<pre><code>${block.text}</code></pre>`;
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
export function markdownView(source: string): Html {
    return html`<div class="chat-md">${markdown(source).map(blockView)}</div>`;
}
