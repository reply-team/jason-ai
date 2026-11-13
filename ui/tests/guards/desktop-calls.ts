import ts from 'typescript';

// The page runs in three places that differ by what each can do: the desktop's own window, a browser tab the desktop
// opened with a bridge, and a browser tab with no desktop. The console is always handed an object of the desktop's whole
// shape, and asks one question, `can(member)`, before it draws or does anything a place may not. The guard reads the
// page's source as TypeScript and holds every use of a member of that object to it: a member the bridge carries needs
// nothing more; any other stands behind the question, in a branch, a function or a wrapper that names that member. The
// members' list is the seam's own declaration, `JasonDesktop` in `src/platform/desktop.d.ts`, so it cannot drift.
//
// Its limit, in plain words: a function that asks `can('<member>')` anywhere in its body counts as behind the question
// for every use of that member in that function, before the use or after it. So the guard holds the intent, that each
// use has its question, and a reader holds the placement, that the question comes first and decides the use.

export interface Members {
    /** The parts of the object and their members: `chat` and its `send`. */
    parts: Map<string, Set<string>>;
    /** The members of the object itself: `onCommand`. */
    top: Set<string>;
}

/** The members of the desktop's object, read from its declaration. */
export function declaredMembers(declaration: string): Members {
    const file = ts.createSourceFile('desktop.d.ts', declaration, ts.ScriptTarget.Latest, true);
    const parts = new Map<string, Set<string>>();
    const top = new Set<string>();
    file.forEachChild((node) => {
        if (!ts.isInterfaceDeclaration(node) || node.name.text !== 'JasonDesktop') return;
        for (const member of node.members) {
            if (!member.name || !ts.isIdentifier(member.name)) continue;
            const name = member.name.text;
            if (ts.isMethodSignature(member)) top.add(name);
            else if (ts.isPropertySignature(member) && member.type && ts.isTypeLiteralNode(member.type)) {
                const set = new Set<string>();
                for (const inner of member.type.members)
                    if (inner.name && ts.isIdentifier(inner.name)) set.add(inner.name.text);
                parts.set(name, set);
            }
        }
    });
    return { parts, top };
}

/** The files that build or hold the objects themselves, and so are the one place the members are written out. */
export const builders = ['platform/places.ts', 'platform/bridge.ts', 'platform/window-desktop.ts'];

/** Files that read one part of the object through a name of their own, each said once here. */
const partAliases: Record<string, { identifier: string; part: string }> = {
    'platform/theme.ts': { identifier: 'seam', part: 'theme' },
    'platform/mode.ts': { identifier: 'seam', part: 'mode' },
};

/** Files that hand one part of the object on whole, as a parameter. */
const partHandedOn = new Set(['console/mount.tsx:theme', 'platform/mode.ts:mode']);

function unwrap(node: ts.Expression): ts.Expression {
    let current = node;
    while (
        ts.isParenthesizedExpression(current) ||
        ts.isAsExpression(current) ||
        ts.isNonNullExpression(current) ||
        ts.isTypeAssertionExpression(current)
    )
        current = current.expression;
    return current;
}

/** Whether an expression is the desktop's object: `desktop`, or what `.desktop` of something reads. */
function isObject(node: ts.Expression): boolean {
    const e = unwrap(node);
    if (ts.isIdentifier(e)) return e.text === 'desktop';
    if (ts.isPropertyAccessExpression(e)) return e.name.text === 'desktop';
    return false;
}

const wrappers = ['gated', 'defineReal'];

/** Whether something around `node` puts the use of `member` behind the question `can('<member>')`. */
function behind(node: ts.Node, member: string, source: ts.SourceFile): boolean {
    const asks = (text: string) => text.includes(`can('${member}')`);
    for (let up: ts.Node | undefined = node.parent; up; up = up.parent) {
        if (ts.isIfStatement(up) && asks(up.expression.getText(source))) return true;
        if (ts.isConditionalExpression(up) && asks(up.condition.getText(source))) return true;
        if (
            ts.isBinaryExpression(up) &&
            up.operatorToken.kind === ts.SyntaxKind.AmpersandAmpersandToken &&
            asks(up.left.getText(source))
        )
            return true;
        if (
            ts.isCallExpression(up) &&
            ts.isIdentifier(up.expression) &&
            wrappers.includes(up.expression.text) &&
            up.arguments.slice(0, 2).some((each) => ts.isStringLiteral(each) && each.text === member)
        )
            return true;
        if (
            (ts.isFunctionDeclaration(up) ||
                ts.isFunctionExpression(up) ||
                ts.isArrowFunction(up) ||
                ts.isMethodDeclaration(up)) &&
            up.body &&
            asks(up.body.getText(source))
        )
            return true;
    }
    return false;
}

/**
 * Every way the file `name` (a path under `src/`) uses the desktop's object outside its place's question, one line each;
 * none when it keeps the rule. `tabMember` says whether a member is one the bridge carries to a tab.
 */
export function problems(
    name: string,
    text: string,
    members: Members,
    tabMember: (member: string) => boolean,
): string[] {
    if (builders.includes(name)) return [];
    const kind = name.endsWith('x') ? ts.ScriptKind.TSX : ts.ScriptKind.TS;
    const file = ts.createSourceFile(name, text, ts.ScriptTarget.Latest, true, kind);
    const found: string[] = [];
    const where = (node: ts.Node) => `${name}:${file.getLineAndCharacterOfPosition(node.getStart(file)).line + 1}`;
    const alias = partAliases[name];

    const check = (node: ts.Node, member: string) => {
        if (tabMember(member) || behind(node, member, file)) return;
        found.push(`${where(node)}: ${member} is used outside the one question can('${member}')`);
    };

    const visit = (node: ts.Node): void => {
        if (ts.isPropertyAccessExpression(node)) {
            const last = node.name.text;
            const base = unwrap(node.expression);
            if (isObject(base) && members.top.has(last)) check(node, last);
            else if (
                ts.isPropertyAccessExpression(base) &&
                isObject(base.expression) &&
                members.parts.get(base.name.text)?.has(last)
            )
                check(node, `${base.name.text}.${last}`);
            else if (
                alias &&
                ts.isIdentifier(base) &&
                base.text === alias.identifier &&
                members.parts.get(alias.part)?.has(last)
            )
                check(node, `${alias.part}.${last}`);
            else if (isObject(base) && members.parts.has(last)) {
                // A part held under a name of its own hides which member is used.
                const parent = node.parent;
                const read = ts.isPropertyAccessExpression(parent) && unwrap(parent.expression) === node;
                if (!read && !partHandedOn.has(`${name}:${last}`))
                    found.push(
                        `${where(node)}: ${last} is held under a name of its own; name the member where it is used`,
                    );
            }
            if (last === 'jasonDesktop')
                found.push(`${where(node)}: window.jasonDesktop is read outside platform/window-desktop.ts`);
        }
        if (
            ts.isVariableDeclaration(node) &&
            ts.isObjectBindingPattern(node.name) &&
            node.initializer &&
            isObject(node.initializer)
        )
            found.push(`${where(node)}: the desktop's object is destructured; name the part and member where used`);
        node.forEachChild(visit);
    };
    visit(file);
    return found;
}
