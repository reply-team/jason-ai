import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import ts from 'typescript';
import { describe, expect, it } from 'vitest';

// The Work items page approves the approval a waiting item waits for through the one gate every place that approves
// shares (`src/console/waiting.ts`, `waiting-model.ts`): the gate reads the approval whole, keeps why it could not, says
// whether Approve is live, and checks a message against its work item at the press. A copy of any of that on the page
// would drift from the gate the chat's cards and Needs you keep, so the guard reads the page's source as TypeScript and
// holds it to the gate: it imports the gate's pieces, never reads an approval whole or checks a message itself, and
// sends `approval.approve` in one place only, inside the act it hands the gate.

const page = join(import.meta.dirname, '..', '..', 'src', 'screens', 'workitems');

function sources(): { name: string; file: ts.SourceFile }[] {
    return readdirSync(page)
        .filter((name) => /\.tsx?$/.test(name) && !/\.test\.tsx?$/.test(name))
        .map((name) => ({
            name,
            file: ts.createSourceFile(name, readFileSync(join(page, name), 'utf8'), ts.ScriptTarget.Latest, true),
        }));
}

function walk(node: ts.Node, visit: (node: ts.Node) => void): void {
    visit(node);
    node.forEachChild((child) => walk(child, visit));
}

/** Every name the page imports, by the module it imports it from. */
function imported(): Map<string, Set<string>> {
    const names = new Map<string, Set<string>>();
    for (const { file } of sources())
        for (const statement of file.statements) {
            if (!ts.isImportDeclaration(statement) || !ts.isStringLiteral(statement.moduleSpecifier)) continue;
            const bindings = statement.importClause?.namedBindings;
            if (!bindings || !ts.isNamedImports(bindings)) continue;
            const from = statement.moduleSpecifier.text;
            const each = names.get(from) ?? new Set<string>();
            for (const element of bindings.elements) each.add((element.propertyName ?? element.name).text);
            names.set(from, each);
        }
    return names;
}

/** Each string literal of the page equal to one of these, with the file it stands in and the node. */
function literals(...texts: string[]): { name: string; node: ts.Node }[] {
    const found: { name: string; node: ts.Node }[] = [];
    for (const { name, file } of sources())
        walk(file, (node) => {
            if ((ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) && texts.includes(node.text))
                found.push({ name, node });
        });
    return found;
}

/** Each identifier of the page that names one of these. */
function identifiers(...names: string[]): string[] {
    const found: string[] = [];
    for (const { name, file } of sources())
        walk(file, (node) => {
            if (ts.isIdentifier(node) && names.includes(node.text)) found.push(`${name}: ${node.text}`);
        });
    return found;
}

/** The call a node is an argument of, at any depth, whose callee is named so. */
function insideCallOf(node: ts.Node, callee: string): boolean {
    for (let at: ts.Node | undefined = node.parent; at; at = at.parent)
        if (ts.isCallExpression(at) && ts.isIdentifier(at.expression) && at.expression.text === callee) return true;
    return false;
}

/** The members of object literals in a source file that carry one of these names, as the gate's answer does. */
function gateMembers(file: ts.SourceFile): string[] {
    const found: string[] = [];
    walk(file, (node) => {
        if (!ts.isObjectLiteralExpression(node)) return;
        for (const member of node.properties)
            if (
                (ts.isPropertyAssignment(member) || ts.isShorthandPropertyAssignment(member)) &&
                ts.isIdentifier(member.name) &&
                ['live', 'reading'].includes(member.name.text)
            )
                found.push(member.name.text);
    });
    return found;
}

/** The calls in a source file whose callee is named so. */
function callsOf(file: ts.SourceFile, callee: string): number {
    let count = 0;
    walk(file, (node) => {
        if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === callee) count++;
    });
    return count;
}

describe('the Work items page approves through the shared gate', () => {
    it('imports the gate’s pieces from the console’s gate, where the chat’s cards and Needs you take them', () => {
        const names = imported();
        expect([...(names.get('../../console/waiting') ?? [])].sort()).toEqual(
            ['approveWhole', 'fillApprovals', 'wholeReads'].sort(),
        );
        expect(names.get('../../console/waiting-model')).toContain('approvalGate');
        expect(names.get('../../console/waiting-model')).toContain('readingSentence');
    });

    it('never reads an approval whole, nor checks a message against its work item, by itself', () => {
        expect(literals('approval.get').map((each) => each.name)).toEqual([]);
        expect(identifiers('checkMessage', 'readItem', 'readOnce', 'keptAndMissing')).toEqual([]);
    });

    it('sends approval.approve in one place, inside the act it hands the gate', () => {
        const sends = literals('approval.approve');
        expect(sends.map((each) => each.name)).toEqual(['store.ts']);
        expect(insideCallOf(sends[0]!.node, 'approveWhole')).toBe(true);
    });

    it('asks the gate whether Approve is live and never states its answer again', () => {
        const calls = sources().reduce((sum, { file }) => sum + callsOf(file, 'approvalGate'), 0);
        expect(calls).toBeGreaterThan(0);
        expect(sources().flatMap(({ name, file }) => gateMembers(file).map((member) => `${name}: ${member}`))).toEqual(
            [],
        );
    });

    it('sees a copy of the gate stated on the page, and a page that does not ask it', () => {
        const copy = ts.createSourceFile(
            'x.tsx',
            "import { approvalGate } from 'gate'; void approvalGate; const gate = whole ? { live: true } : { live: false, reading: true };",
            ts.ScriptTarget.Latest,
            true,
        );
        expect(callsOf(copy, 'approvalGate')).toBe(0);
        expect(gateMembers(copy)).toEqual(['live', 'live', 'reading']);
        const asks = ts.createSourceFile(
            'x.tsx',
            'const gate = approvalGate(whole, unread);',
            ts.ScriptTarget.Latest,
            true,
        );
        expect([callsOf(asks, 'approvalGate'), gateMembers(asks)]).toEqual([1, []]);
    });

    it('sees a send outside the gate when there is one', () => {
        const file = ts.createSourceFile(
            'x.ts',
            "approveWhole(api, whole, () => go()); api.call('approval.approve', {});",
            ts.ScriptTarget.Latest,
            true,
        );
        const found: ts.Node[] = [];
        walk(file, (node) => {
            if (ts.isStringLiteral(node) && node.text === 'approval.approve') found.push(node);
        });
        expect(found.map((node) => insideCallOf(node, 'approveWhole'))).toEqual([false]);
    });
});
