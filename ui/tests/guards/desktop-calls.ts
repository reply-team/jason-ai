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

/** A member read by a name, as `a.b` or `a['b']`: its base and its name. */
function access(node: ts.Node): { base: ts.Expression; name: string } | null {
    if (ts.isPropertyAccessExpression(node)) return { base: unwrap(node.expression), name: node.name.text };
    if (
        ts.isElementAccessExpression(node) &&
        (ts.isStringLiteral(node.argumentExpression) || ts.isNoSubstitutionTemplateLiteral(node.argumentExpression))
    )
        return { base: unwrap(node.expression), name: node.argumentExpression.text };
    return null;
}

/** What a type annotation says about the desktop: the whole object, one part of it, or nothing. */
function typed(type: ts.TypeNode | undefined, parts: Map<string, Set<string>>): 'object' | string | null {
    if (!type) return null;
    let found: 'object' | string | null = null;
    const look = (node: ts.Node): void => {
        if (ts.isIndexedAccessTypeNode(node) && /\bJasonDesktop$/.test(node.objectType.getText())) {
            if (ts.isLiteralTypeNode(node.indexType) && ts.isStringLiteral(node.indexType.literal)) {
                const part = node.indexType.literal.text;
                if (parts.has(part)) found = part;
            }
            return;
        }
        if (ts.isTypeReferenceNode(node) && node.typeName.getText() === 'JasonDesktop') found = 'object';
        node.forEachChild(look);
    };
    look(type);
    return found;
}

/** Whether the branch of a condition that asks for `member` is the one where the answer was yes; null if it does not ask. */
function sideOf(condition: ts.Expression, source: ts.SourceFile, member: string): boolean | null {
    const text = condition.getText(source);
    if (!text.includes(`can('${member}')`)) return null;
    return !text.includes(`!can('${member}')`);
}

const wrappers = ['gated', 'defineReal'];

/** Whether something around `node` puts the use of `member` behind the question `can('<member>')`. */
function behind(node: ts.Node, member: string, source: ts.SourceFile): boolean {
    const asks = (text: string) => text.includes(`can('${member}')`);
    let child: ts.Node = node;
    for (let up: ts.Node | undefined = node.parent; up; child = up, up = up.parent) {
        if (ts.isIfStatement(up)) {
            const yes = sideOf(up.expression, source, member);
            // The branch the question was answered yes in passes; the other refuses, whatever else the function asks.
            if (yes !== null && child !== up.expression) return child === up.thenStatement ? yes : !yes;
        }
        if (ts.isConditionalExpression(up)) {
            const yes = sideOf(up.condition, source, member);
            if (yes !== null && child !== up.condition) return child === up.whenTrue ? yes : !yes;
        }
        if (ts.isBinaryExpression(up) && up.operatorToken.kind === ts.SyntaxKind.AmpersandAmpersandToken) {
            const yes = sideOf(up.left, source, member);
            if (yes !== null && child === up.right) return yes;
        }
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

    // Names this file gives the object or a part of it by their type, under any name.
    const objects = new Set(['desktop']);
    const partNames = new Map<string, string>();
    const collect = (node: ts.Node): void => {
        if (
            (ts.isParameter(node) || ts.isVariableDeclaration(node) || ts.isPropertySignature(node)) &&
            ts.isIdentifier(node.name)
        ) {
            const typedAs = typed(node.type, members.parts);
            if (typedAs === 'object') objects.add(node.name.text);
            else if (typedAs !== null) partNames.set(node.name.text, typedAs);
        }
        node.forEachChild(collect);
    };
    collect(file);

    const isObject = (node: ts.Expression): boolean => {
        const e = unwrap(node);
        if (ts.isIdentifier(e)) return objects.has(e.text);
        const a = access(e);
        return a !== null && a.name === 'desktop';
    };
    /** The part of the object an expression is, if it is one: `desktop.chat`, or a name typed as one. */
    const partOf = (node: ts.Expression): string | null => {
        const e = unwrap(node);
        if (ts.isIdentifier(e)) return partNames.get(e.text) ?? null;
        const a = access(e);
        if (a !== null && isObject(a.base) && members.parts.has(a.name)) return a.name;
        return null;
    };

    const check = (node: ts.Node, member: string) => {
        if (tabMember(member) || behind(node, member, file)) return;
        found.push(`${where(node)}: ${member} is used outside the one question can('${member}')`);
    };

    const visit = (node: ts.Node): void => {
        const a = access(node);
        if (a !== null) {
            const part = partOf(a.base);
            if (isObject(a.base) && members.top.has(a.name)) check(node, a.name);
            else if (part !== null && members.parts.get(part)?.has(a.name)) check(node, `${part}.${a.name}`);
            else if (
                alias &&
                ts.isIdentifier(a.base) &&
                a.base.text === alias.identifier &&
                members.parts.get(alias.part)?.has(a.name)
            )
                check(node, `${alias.part}.${a.name}`);
            else if (isObject(a.base) && members.parts.has(a.name)) {
                // A part held under a name of its own hides which member is used.
                const read = access(node.parent)?.base === node;
                if (!read && !partHandedOn.has(`${name}:${a.name}`))
                    found.push(
                        `${where(node)}: ${a.name} is held under a name of its own; name the member where it is used`,
                    );
            }
            if (a.name === 'jasonDesktop')
                found.push(`${where(node)}: the window's object is read outside platform/window-desktop.ts`);
        }
        if (ts.isVariableDeclaration(node) && node.initializer) {
            const init = unwrap(node.initializer);
            if (ts.isObjectBindingPattern(node.name)) {
                if (isObject(init) || partOf(init) !== null)
                    found.push(
                        `${where(node)}: the desktop's object is destructured; name the part and member where used`,
                    );
                if (node.name.elements.some((each) => (each.propertyName ?? each.name).getText() === 'jasonDesktop'))
                    found.push(`${where(node)}: the window's object is read outside platform/window-desktop.ts`);
            } else if (
                ts.isIdentifier(node.name) &&
                node.name.text !== 'desktop' &&
                alias?.identifier !== node.name.text
            ) {
                // A name of its own for the object or a part of it, where its type says nothing.
                if (isObject(init) || (partOf(init) !== null && ts.isIdentifier(init)))
                    found.push(
                        `${where(node)}: the desktop's object is held under the name ${node.name.text}; use it as desktop`,
                    );
            }
        }
        node.forEachChild(visit);
    };
    visit(file);
    return found;
}
