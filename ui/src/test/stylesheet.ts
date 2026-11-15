import { readFileSync } from 'node:fs';

// The console's stylesheet as it applies to an element of the page, for a screen test that holds a look to its numbers.
// The test run hands a stylesheet import back empty and happy-dom lays nothing out, so the file is read and each rule's
// selectors are matched against the element, the declarations taken in the cascade's order (specificity, then source
// order). Only the rules at the top level are read: a rule under `@media` (a narrow window, the dark theme the system
// asks for) is not the light, wide page a test draws. The run starts in ui/.

interface Rule {
    selectors: string[];
    declarations: [string, string][];
    order: number;
}

const read = (path: string): Rule[] => {
    const text = readFileSync(path, 'utf8').replace(/[/][*][^]*?[*][/]/g, '');
    const rules: Rule[] = [];
    let depth = 0;
    let start = 0;
    let skipped = -1;
    for (let at = 0; at < text.length; at++) {
        const char = text[at];
        if (char === '{') {
            const head = text.slice(start, at).trim();
            if (depth === 0 && head.startsWith('@')) skipped = depth;
            depth++;
            if (skipped === -1) {
                const end = text.indexOf('}', at);
                const body = text.slice(at + 1, end);
                rules.push({
                    selectors: head.split(',').map((each) => each.trim()),
                    declarations: body
                        .split(';')
                        .map((each) => each.trim())
                        .filter((each) => each.includes(':'))
                        .map((each) => {
                            const colon = each.indexOf(':');
                            return [each.slice(0, colon).trim(), each.slice(colon + 1).trim()] as [string, string];
                        }),
                    order: rules.length,
                });
                at = end;
                depth--;
                start = at + 1;
                continue;
            }
            start = at + 1;
        } else if (char === '}') {
            depth--;
            if (depth === skipped) skipped = -1;
            start = at + 1;
        }
    }
    return rules;
};

/** A selector's specificity as one number: ids, then classes, attributes and pseudo-classes, then types. */
function specificity(selector: string): number {
    const bare = selector.replace(/"[^"]*"|'[^']*'/g, '');
    const ids = (bare.match(/#[\w-]+/g) ?? []).length;
    const classes = (bare.match(/\.[\w-]+|\[[^\]]*\]|:(?!:)(?!not\(|is\(|where\()[\w-]+/g) ?? []).length;
    const types = (bare.match(/(^|[\s>+~(])[a-z][\w-]*/gi) ?? []).length;
    return ids * 10_000 + classes * 100 + types;
}

const matches = (element: Element, selector: string): boolean => {
    try {
        return element.matches(selector);
    } catch {
        // A pseudo-element or a selector the test's document does not know names no element of the page.
        return false;
    }
};

/** The console's stylesheet, read once. */
const consoleRules = read('src/console/console.css');

/**
 * The value each property takes on `element` from the console's stylesheet, by the cascade; a property no rule sets is
 * absent. Custom properties are not resolved: `var(--line)` is what the rule says.
 */
export function styleOf(element: Element): Record<string, string> {
    const applied: { value: [string, string]; weight: number; order: number }[] = [];
    for (const rule of consoleRules) {
        const weight = Math.max(
            -1,
            ...rule.selectors.filter((each) => matches(element, each)).map((each) => specificity(each)),
        );
        if (weight < 0) continue;
        for (const declaration of rule.declarations) applied.push({ value: declaration, weight, order: rule.order });
    }
    applied.sort((a, b) => a.weight - b.weight || a.order - b.order);
    return Object.fromEntries(applied.map((each) => each.value));
}

/** The value a custom property is given on `.jc-console` at the top level of the console's stylesheet. */
export function tokenOf(name: string): string | undefined {
    let value: string | undefined;
    for (const rule of consoleRules)
        if (rule.selectors.includes('.jc-console'))
            for (const [property, said] of rule.declarations) if (property === name) value = said;
    return value;
}
