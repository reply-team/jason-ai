// Pure, and run by the build: the browser shell's stylesheet, scoped to the element the console shows its screens in.
// The page runs under `default-src 'self'`, so no stylesheet is injected at run time; the scoped copy is a file of the
// build like any other.

/** The first compound of a selector, up to the first combinator, and the rest. */
function firstCompound(selector: string): [string, string] {
    const match = /^[^\s>+~]+/.exec(selector);
    const head = match?.[0] ?? '';
    return [head, selector.slice(head.length)];
}

/**
 * One selector of the shell's, made to match inside `scope` only. The document's own selectors (`:root`, `html`,
 * `body`) become the scope, and a state of the document's (`:root[data-theme='dark']`) stays on the document with the
 * scope beneath it.
 */
export function scopeSelector(selector: string, scope: string): string {
    const trimmed = selector.trim();
    const [head, rest] = firstCompound(trimmed);
    if (head === ':root' || head === 'html' || head === 'body') return `${scope}${rest}`;
    if (head.startsWith(':root')) return `${head} ${scope}${rest}`;
    return `${scope} ${trimmed}`;
}

interface CssRule {
    selectors: string[];
    parent?: { type: string; name?: string } | undefined;
}
interface CssRoot {
    source?: { input: { file?: string } };
    walkRules(visit: (rule: CssRule) => void): void;
}

/**
 * The build's step: every rule of a stylesheet whose file name ends with `fileSuffix` is scoped, after its imports were
 * inlined; the rules of a `@keyframes` are left alone.
 */
export function scopePlugin(fileSuffix: string, scope: string) {
    return {
        postcssPlugin: 'jason-scope',
        OnceExit(root: CssRoot) {
            const file = root.source?.input.file?.replace(/\\/g, '/') ?? '';
            if (!file.endsWith(fileSuffix)) return;
            root.walkRules((rule) => {
                if (rule.parent?.type === 'atrule' && /keyframes$/i.test(rule.parent.name ?? '')) return;
                rule.selectors = rule.selectors.map((selector) => scopeSelector(selector, scope));
            });
        },
    };
}
