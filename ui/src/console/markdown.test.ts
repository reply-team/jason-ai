import { describe, expect, it } from 'vitest';
import { markdown, type Block, type Inline } from './markdown';

const text = (value: string): Inline => ({ kind: 'text', text: value });
const paragraph = (...children: Inline[]): Block => ({ kind: 'paragraph', children });
const plain = (value: string): Block => paragraph(text(value));

describe("the chat's Markdown subset", () => {
    it('reads paragraphs, split by a blank line, with their line breaks kept', () => {
        expect(markdown('One line\nthe next\n\nA second paragraph')).toEqual([
            plain('One line\nthe next'),
            plain('A second paragraph'),
        ]);
    });

    it('reads emphasis, strong emphasis and inline code', () => {
        expect(markdown('An *aside*, a **point**, an _aside_, a __point__ and `jason status`.')).toEqual([
            paragraph(
                text('An '),
                { kind: 'em', children: [text('aside')] },
                text(', a '),
                { kind: 'strong', children: [text('point')] },
                text(', an '),
                { kind: 'em', children: [text('aside')] },
                text(', a '),
                { kind: 'strong', children: [text('point')] },
                text(' and '),
                { kind: 'code', text: 'jason status' },
                text('.'),
            ),
        ]);
    });

    it('keeps an underscore inside a word, an id or an event name, as text', () => {
        expect(markdown('The line inbound_reply_observed for cmp_01_x.')).toEqual([
            plain('The line inbound_reply_observed for cmp_01_x.'),
        ]);
    });

    it('keeps inline code whole, markers and backslashes inside it included', () => {
        expect(markdown('Run `a *b* \\c` and ``x ` y``')).toEqual([
            paragraph(text('Run '), { kind: 'code', text: 'a *b* \\c' }, text(' and '), {
                kind: 'code',
                text: 'x ` y',
            }),
        ]);
    });

    it('reads a backslash before a marker as the marker itself', () => {
        expect(markdown('\\*not emphasis\\*')).toEqual([plain('*not emphasis*')]);
    });

    it('reads headings of six levels, with a closing run of # dropped', () => {
        expect(markdown('# One\n## Two ##\n###### Six')).toEqual([
            { kind: 'heading', level: 1, children: [text('One')] },
            { kind: 'heading', level: 2, children: [text('Two')] },
            { kind: 'heading', level: 6, children: [text('Six')] },
        ]);
        expect(markdown('#hashtag')).toEqual([plain('#hashtag')]);
        expect(markdown('####### Seven')).toEqual([plain('####### Seven')]);
    });

    it('reads a fenced code block as its text, markers and all, closed or not', () => {
        expect(markdown('```js\nconst a = *b*;\n<b>x</b>\n```\nafter')).toEqual([
            { kind: 'code', text: 'const a = *b*;\n<b>x</b>' },
            plain('after'),
        ]);
        expect(markdown('~~~\nopen to the end')).toEqual([{ kind: 'code', text: 'open to the end' }]);
    });

    it('reads bulleted and numbered lists, with the number a list starts at', () => {
        expect(markdown('- one\n- **two**\n\n3. three\n4. four')).toEqual([
            {
                kind: 'list',
                ordered: false,
                start: 1,
                items: [[plain('one')], [paragraph({ kind: 'strong', children: [text('two')] })]],
            },
            { kind: 'list', ordered: true, start: 3, items: [[plain('three')], [plain('four')]] },
        ]);
    });

    it('reads a list nested in an item by its indentation', () => {
        expect(markdown('1. first\n   - a\n   - b\n2. second')).toEqual([
            {
                kind: 'list',
                ordered: true,
                start: 1,
                items: [
                    [plain('first'), { kind: 'list', ordered: false, start: 1, items: [[plain('a')], [plain('b')]] }],
                    [plain('second')],
                ],
            },
        ]);
    });

    it('reads a block quote, and a list inside it', () => {
        expect(markdown('> Hi Ana,\n>\n> - a point\n> - another')).toEqual([
            {
                kind: 'quote',
                children: [
                    plain('Hi Ana,'),
                    { kind: 'list', ordered: false, start: 1, items: [[plain('a point')], [plain('another')]] },
                ],
            },
        ]);
    });

    it('reads a pipe table with a header, and emphasis inside a cell', () => {
        expect(markdown('| Name | Stage |\n|---|:---:|\n| Ana | *replied* |\n| Bo \\| Co | sent |')).toEqual([
            {
                kind: 'table',
                head: [[text('Name')], [text('Stage')]],
                rows: [
                    [[text('Ana')], [{ kind: 'em', children: [text('replied')] }]],
                    [[text('Bo | Co')], [text('sent')]],
                ],
            },
        ]);
    });

    it('draws a table with ragged rows, or with no header separator, as the text it is', () => {
        const ragged = '| a | b |\n|---|---|\n| 1 | 2 | 3 |';
        expect(markdown(ragged)).toEqual([plain(ragged)]);
        expect(markdown('| a | b |\n| 1 | 2 |')).toEqual([plain('| a | b |\n| 1 | 2 |')]);
        expect(markdown('| a | b |\n|---|')).toEqual([plain('| a | b |\n|---|')]);
    });

    it('draws a link as its text and its address, both text', () => {
        expect(markdown('See [the guide](https://example.test/guide).')).toEqual([
            paragraph(text('See '), { kind: 'link', text: 'the guide', href: 'https://example.test/guide' }, text('.')),
        ]);
    });
});

describe('what the subset does not hold', () => {
    it('keeps raw HTML, a script and an image as the text they are', () => {
        const source = '<script>alert(1)</script> <img src=x onerror=alert(1)> ![x](https://example.test/a.png)';
        expect(markdown(source)).toEqual([plain(source)]);
    });

    it('keeps a raw HTML block, lines and all, as the text it is', () => {
        const block = '<div>\n<iframe src="https://example.test"></iframe>\n</div>';
        expect(markdown(block)).toEqual([plain(block)]);
    });

    it('keeps a fence inside a block quote as code, and its language word out of what is drawn', () => {
        expect(markdown('> ```html" onclick="x\n> <b>bold</b>\n> ```')).toEqual([
            { kind: 'quote', children: [{ kind: 'code', text: '<b>bold</b>' }] },
        ]);
    });

    it('keeps a javascript link an address of text, never anything else', () => {
        expect(markdown('[click](javascript:alert(1))')).toEqual([plain('[click](javascript:alert(1))')]);
        expect(markdown('[click](javascript:alert`1`)')).toEqual([
            paragraph({ kind: 'link', text: 'click', href: 'javascript:alert`1`' }),
        ]);
    });

    it('keeps markers it cannot pair, or pairs nested past what it reads, as text', () => {
        expect(markdown('***both*** and **open and *half')).toEqual([plain('***both*** and **open and *half')]);
        expect(markdown('*a **b** c*')).toEqual([
            paragraph({ kind: 'em', children: [text('a '), { kind: 'strong', children: [text('b')] }, text(' c')] }),
        ]);
    });

    it('stops reading nesting at a fixed depth and draws the rest as text', () => {
        const deep = `${'>'.repeat(40)} deep`;
        let blocks = markdown(deep);
        let depth = 0;
        while (blocks[0]?.kind === 'quote') {
            blocks = blocks[0].children;
            depth++;
        }
        expect(depth).toBeLessThanOrEqual(8);
        expect(blocks).toEqual([plain(`${'>'.repeat(40 - depth)} deep`)]);
    });

    it('reads a long run of unpaired markers in a moment', () => {
        const source = '*a _b `c [d '.repeat(20_000);
        const started = performance.now();
        markdown(source);
        expect(performance.now() - started).toBeLessThan(2_000);
    });

    it('reads Windows line ends as line ends', () => {
        expect(markdown('# A\r\nb')).toEqual([{ kind: 'heading', level: 1, children: [text('A')] }, plain('b')]);
    });
});
