import { h, type ComponentChild } from 'preact';

// Structure and emphasis are rebuilt; a link keeps its address when it is a web or mail one, and is plain text when it
// is not. Anything else that is an element is unwrapped to its text, and what runs, loads, styles or asks for input is
// dropped with what it holds.
const kept = new Set([
    'p',
    'br',
    'div',
    'span',
    'b',
    'strong',
    'i',
    'em',
    'u',
    'h1',
    'h2',
    'h3',
    'h4',
    'h5',
    'h6',
    'ul',
    'ol',
    'li',
    'blockquote',
    'pre',
    'code',
    'hr',
    'table',
    'thead',
    'tbody',
    'tr',
    'th',
    'td',
    'a',
]);
const dropped = new Set([
    'script',
    'style',
    'iframe',
    'frame',
    'object',
    'embed',
    'form',
    'input',
    'button',
    'select',
    'textarea',
    'img',
    'picture',
    'video',
    'audio',
    'source',
    'svg',
    'math',
    'template',
    'noscript',
    'link',
    'meta',
    'base',
    'head',
    'title',
]);
const empty = new Set(['br', 'hr']);
const safeLink = /^(?:https?:|mailto:)/i;
// Markup is a tag this page knows; `2 < 3`, `<3` and `<a@example.com>` in a plain-text message are text.
const markup = new RegExp(`</?(?:${[...kept, ...dropped].join('|')})(?=[\\s/>])`, 'i');
// Inline styles and handlers are stripped before parsing only to keep the console quiet: the page's policy reports
// every one an inert parse meets. Nothing depends on this for safety, because the rebuild keeps no attribute at all.
const inline = /\s(?:style|on[a-z]+)\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]+)/gi;
const tags = /<[^>]*>/g;

function rebuild(node: Node, key: number): ComponentChild {
    if (node.nodeType === Node.TEXT_NODE) return node.textContent;
    if (node.nodeType !== Node.ELEMENT_NODE) return null;
    const element = node as Element;
    const tag = element.tagName.toLowerCase();
    if (dropped.has(tag)) return null;
    if (empty.has(tag)) return h(tag, { key });
    const children = Array.from(element.childNodes).map(rebuild);
    if (!kept.has(tag)) return h('span', { key }, children);
    if (tag === 'a') {
        const href = element.getAttribute('href')?.trim() ?? '';
        return safeLink.test(href)
            ? h('a', { key, href, target: '_blank', rel: 'noopener noreferrer' }, children)
            : h('span', { key }, children);
    }
    return h(tag, { key }, children);
}

/**
 * A message body as the provider holds it, shown without ever being inserted as HTML: the runtime passes the body
 * through untouched, so the page parses it in an inert document — where nothing runs or loads — and rebuilds only the
 * elements it keeps, with no attribute but a safe link's address. A body with no tag the page knows is text, shown
 * with its line breaks.
 */
export function MessageBody({ body }: { body: string }) {
    if (!markup.test(body)) {
        const lines = body.split(/\r?\n/);
        return (
            <div class="preview" data-testid="message-body">
                {lines.flatMap((line, at) => (at === 0 ? [line] : [<br key={at} />, line]))}
            </div>
        );
    }
    const quiet = body.replace(tags, (tag) => tag.replace(inline, ''));
    const parsed = new DOMParser().parseFromString(quiet, 'text/html');
    return (
        <div class="preview" data-testid="message-body">
            {Array.from(parsed.body.childNodes).map(rebuild)}
        </div>
    );
}
