import ts from 'typescript';

// The console draws its panels from markup, under a CSP that refuses inline handlers and inline styles. What it draws
// was written by agents and prospects, so the guard reads the console's source as TypeScript and holds it to the one
// way `src/console/html.ts` gives: markup only from the `html` tag, which escapes what it interpolates; written into the
// page only by `paint`; no handler and no style in markup, and none in JSX either.

/** The file that is the console's one way to markup: it alone may write markup into the page. */
export const markupFile = 'html.ts';

const handler = /\bon[a-z]+\s*=\s*["'`]/i;
const styleAttribute = /\bstyle\s*=\s*["'`]/i;
const tag = /<\/?[a-z!][^<>]*>?/i;
const sinks = new Set(['innerHTML', 'outerHTML', 'insertAdjacentHTML', 'dangerouslySetInnerHTML', 'write', 'writeln']);

function text(node: ts.Node): string | null {
    if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node) || ts.isJsxText(node)) return node.text;
    if (ts.isTemplateExpression(node))
        return [node.head.text, ...node.templateSpans.map((span) => span.literal.text)].join('${}');
    return null;
}

/** Whether a literal is the `html` tag's own template, the one place markup is made. */
function taggedHtml(node: ts.Node): boolean {
    const parent = node.parent;
    return (
        !!parent && ts.isTaggedTemplateExpression(parent) && ts.isIdentifier(parent.tag) && parent.tag.text === 'html'
    );
}

/** Every way the file `name` breaks the console's rule for markup, one line each; none when it keeps it. */
export function problems(name: string, source: string): string[] {
    const file = ts.createSourceFile(
        name,
        source,
        ts.ScriptTarget.Latest,
        true,
        name.endsWith('x') ? ts.ScriptKind.TSX : ts.ScriptKind.TS,
    );
    const found: string[] = [];
    const at = (node: ts.Node) => `${name}:${file.getLineAndCharacterOfPosition(node.getStart(file)).line + 1}`;
    const visit = (node: ts.Node): void => {
        const words = text(node);
        if (words !== null) {
            if (handler.test(words)) found.push(`${at(node)} an inline handler in markup`);
            if (styleAttribute.test(words)) found.push(`${at(node)} a style attribute in markup`);
            if (!ts.isJsxText(node) && tag.test(words) && !taggedHtml(node))
                found.push(`${at(node)} markup outside the html tag, where nothing escapes what it interpolates`);
        }
        if (ts.isJsxAttribute(node) && ts.isIdentifier(node.name) && node.name.text === 'style')
            found.push(`${at(node)} a style attribute in JSX`);
        if (ts.isJsxAttribute(node) && ts.isIdentifier(node.name) && node.name.text === 'dangerouslySetInnerHTML')
            found.push(`${at(node)} markup written into the page outside ${markupFile}`);
        if (
            name !== markupFile &&
            (ts.isPropertyAccessExpression(node) || ts.isElementAccessExpression(node)) &&
            sinks.has(
                ts.isPropertyAccessExpression(node)
                    ? node.name.text
                    : ts.isStringLiteral(node.argumentExpression)
                      ? node.argumentExpression.text
                      : '',
            )
        )
            found.push(`${at(node)} markup written into the page outside ${markupFile}`);
        if (
            name !== markupFile &&
            ts.isPropertyAccessExpression(node) &&
            node.name.text === 'made' &&
            ts.isIdentifier(node.expression) &&
            node.expression.text === 'Html'
        )
            found.push(`${at(node)} markup made outside the html tag`);
        ts.forEachChild(node, visit);
    };
    visit(file);
    return found;
}
