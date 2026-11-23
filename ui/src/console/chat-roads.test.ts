import { readdirSync, readFileSync } from 'node:fs';
import { join, sep } from 'node:path';
import ts from 'typescript';
import { describe, expect, it } from 'vitest';

// Every act of the console that shows a chat, or Start, and whether the box it shows takes the focus. A chat the person
// opens by an act of their own has the caret in its box (`box-focus.ts`); the only ones that show a chat and leave its box
// alone are Try again, an act whose own control takes the focus (a rename's field, a removal's question), and a change
// that is not the person's act. The table is the list, and the test reads the source for every call that can show a
// chat or Start, so a road added without a row here, or a row whose road went, is red.

/**
 * What a call does with the box:
 * - `yes`: a person's act that shows a chat or Start, whose box takes the focus; `proof` names the screen tests that
 *   show it, in `box-focus.screen.test.tsx`.
 * - `no`: it shows a chat or Start, and its box does not take the focus; `why` says why.
 * - `passes`: the dock's own move, inside a road: the box takes the focus exactly when the act that called it noted one.
 * - `none`: the call can show nothing that has a box; `why` says what it shows.
 */
type Row =
    | { file: string; at: string; call: string; box: 'yes'; proof: readonly string[] }
    | { file: string; at: string; call: string; box: 'no' | 'passes' | 'none'; why: string };

/** The screen tests that prove a road, by their names. */
const proof = {
    chatsRow: 'from a row of Chats has the caret in its box, at the end of the words left there',
    agentsRow: 'from a row of Agents has the caret in the agent’s box, with its conversation and with none yet',
    feedRow: 'from the Feed’s row has the caret in the Feed’s box',
    plusChats: 'from the plus over CHATS has the caret in the new chat’s box',
    plusAgents: 'from the plus over AGENTS has the caret in the box of the new agent’s opening',
    search: 'from a choice in Search has the caret in its box: a conversation, and an agent with none yet',
    searchPages: 'from Search’s Feed and its New chat has the caret in the box',
    searchAgent: 'from Search’s New GTM agent has the caret in the box of the new agent’s opening',
    rowMenuOpen: 'from a row menu’s Open has the caret in its box',
    rowMenuFresh: 'from a row menu’s Start conversation fresh has the caret in its box',
    windowNewChat: 'from the window’s New chat, by its menu or its key, has the caret in the new chat’s box',
    back: 'reached again by Back has the caret in its box, as one opened from its row',
    agentsPageChat: 'from a row’s Chat on the Agents page has the caret in the agent’s box',
    agentsPageOpen: 'from a row’s Open on the Agents page, which opens the agent on its Chat, has the caret in its box',
    agentsPageNew: 'from the Agents page’s New chat, with no agent yet, has the caret in the new chat’s box',
    chatTab: 'from its header’s Chat tab, by a click or Enter, has the caret in its box',
    agentLink: 'by a link to it the desktop hands the console has the caret in its box',
    feedLink: 'from a notification or a card has the caret in the Feed’s box',
    mark: 'has the caret in its box, at the end of the words typed there and not sent',
    feedLine: 'from a Feed line’s agent has the caret in the agent’s box',
    dashboardLine: 'from the Dashboard’s line about an agent has the caret in the agent’s box',
    dashboardNew: 'from the Dashboard’s New GTM agent has the caret in the box of the new agent’s opening',
    needsChat: 'from Needs you’s Open the chat has the caret in the agent’s box',
    cardPage:
        'from a data card’s link to an agent’s Chat has the caret in its box; a link to a page of no chat moves none',
    classic: 'from a page’s New chat, handed over by the classic pages, has the caret in the new chat’s box',
    startSend: 'from Start’s send has the caret in the box of the chat it made',
    settingsBackChat: 'from Settings’ Back to the chat it was opened from has the caret in that chat’s box',
    settingsBackStart: 'from Settings’ Back to Start has the caret in Start’s box',
} as const;

/** The dock's own move, which takes the act its caller noted, or none, on to the box (`giveBox`). */
const passes =
    'The dock’s own move inside a road: the box takes the focus exactly when the act that called it noted one.';

const rows: readonly Row[] = [
    // The agent's header: its Chat tab, and its own menu.
    {
        file: 'console/agent-head.tsx',
        at: 'AgentHead',
        call: 'here().dock.page(agentChatRoute(id), openedHere())',
        box: 'yes',
        proof: [proof.chatTab],
    },
    {
        file: 'console/agent-head.tsx',
        at: 'ConversationMenu › show',
        call: 'navigate(agentChatRoute(agent))',
        box: 'no',
        why: 'The agent’s own menu shows its Chat for Rename or Remove: the name’s field, or the question’s Keep, takes the focus.',
    },
    // The Agents page.
    {
        file: 'console/campaigns.ts',
        at: 'campaigns.chat',
        call: 'here().dock.chat({ id: campaign.id, name: campaign.name }, openedHere())',
        box: 'yes',
        proof: [proof.agentsPageChat],
    },
    {
        file: 'console/campaigns.ts',
        at: 'campaigns.open',
        call: 'here().dock.page(parseRoute(`#/campaigns/${encodeURIComponent(id)}`), openedHere())',
        box: 'yes',
        proof: [proof.agentsPageOpen],
    },
    {
        file: 'console/campaigns.ts',
        at: 'campaigns.start',
        call: 'here().dock.chat(undefined, openedHere())',
        box: 'yes',
        proof: [proof.agentsPageNew],
    },
    // A data card's link: the dock gives the box the focus only where the address is an agent's Chat.
    {
        file: 'console/card-frame.ts',
        at: 'card.page',
        call: 'here().dock.page(parseRoute(hash), openedHere())',
        box: 'yes',
        proof: [proof.cardPage],
    },
    // A Feed line's agent, and the Dashboard's line about one.
    {
        file: 'console/chat.tsx',
        at: 'feed.campaign',
        call: 'here().dock.chat({ id, name }, openedHere())',
        box: 'yes',
        proof: [proof.feedLine, proof.dashboardLine],
    },
    // The console's shell and its dock.
    {
        file: 'console/console.tsx',
        at: 'ChatUnopened',
        call: 'here().dock.page(agentChatRoute(agent))',
        box: 'no',
        why: 'Try again: it asks once more for a conversation the desktop could not open, and the focus stays on it.',
    },
    {
        file: 'console/console.tsx',
        at: 'chatAbout',
        call: 'here().dock.chat(campaign, openedHere())',
        box: 'yes',
        proof: [proof.classic],
    },
    {
        file: 'console/console.tsx',
        at: 'Shell',
        call: "reveal('start')",
        box: 'no',
        why: 'The window opening on Start is no act in the page: Start’s own mount gives its box the focus.',
    },
    {
        file: 'console/console.tsx',
        at: 'Shell',
        call: 'reveal(named.panel)',
        box: 'none',
        why: 'The window opening at a panel’s address: Needs you, the Agents page or a page, never a chat or Start.',
    },
    {
        file: 'console/console.tsx',
        at: 'Shell',
        call: 'showAgentChat(named.agent)',
        box: 'no',
        why: 'The window opening at an agent’s address is no act in the page.',
    },
    {
        file: 'console/console.tsx',
        at: 'Shell › opened',
        call: 'dock.page(place.route, act)',
        box: 'yes',
        proof: [proof.agentLink],
    },
    {
        file: 'console/console.tsx',
        at: 'Shell › opened',
        call: 'dock.show(place.panel)',
        box: 'none',
        why: 'A link to Needs you or to the Agents page, never to Start.',
    },
    {
        file: 'console/console.tsx',
        at: 'Shell › opened',
        call: 'dock.showChat(held, act)',
        box: 'yes',
        proof: [proof.feedLink],
    },
    {
        file: 'console/console.tsx',
        at: 'Shell › stops',
        call: 'dock.chat(undefined, openedHere())',
        box: 'yes',
        proof: [proof.windowNewChat],
    },
    {
        file: 'console/console.tsx',
        at: 'Shell › stops',
        call: 'dock.page(to)',
        box: 'none',
        why: 'A command of the window names a screen (Performance, Work, People, System, Settings), never an agent’s Chat.',
    },
    {
        file: 'console/console.tsx',
        at: 'Shell › stops',
        call: 'dock.show(to)',
        box: 'none',
        why: 'A command of the window names a panel of no box (Needs you, the Agents page, Activity, Connect).',
    },
    {
        file: 'console/console.tsx',
        at: 'chatMade',
        call: 'reveal(now)',
        box: 'no',
        why: 'An unmade chat giving way to the conversation made for it is no act; a box the person was typing in keeps the focus by the chat’s own road.',
    },
    {
        file: 'console/console.tsx',
        at: 'followHistory',
        call: 'showAgentChat(agent, opened)',
        box: 'yes',
        proof: [proof.back, proof.agentLink],
    },
    {
        file: 'console/console.tsx',
        at: 'makeDock › chat',
        call: 'showAgentChat(campaign.id, opened)',
        box: 'passes',
        why: passes,
    },
    {
        file: 'console/console.tsx',
        at: 'makeDock › dropChat',
        call: "reveal('start')",
        box: 'no',
        why: 'A chat taken away leaves Start in its place: the person removed a chat and opened nothing.',
    },
    {
        file: 'console/console.tsx',
        at: 'makeDock › page',
        call: 'showAgentChat(agent, opened)',
        box: 'passes',
        why: passes,
    },
    {
        file: 'console/console.tsx',
        at: 'makeDock › showChat',
        call: 'reveal(chatPane(id))',
        box: 'passes',
        why: passes,
    },
    { file: 'console/console.tsx', at: 'restore', call: 'reveal(unaddressed)', box: 'passes', why: passes },
    { file: 'console/console.tsx', at: 'showAgentChat', call: 'reveal(chatPane(chatId))', box: 'passes', why: passes },
    { file: 'console/console.tsx', at: 'showUnaddressed', call: 'reveal(pane)', box: 'passes', why: passes },
    // The Dashboard's New GTM agent.
    {
        file: 'console/dashboard.ts',
        at: 'dash.new',
        call: 'newAgent(undefined, openedHere())',
        box: 'yes',
        proof: [proof.dashboardNew],
    },
    // Needs you's Open the chat.
    {
        file: 'console/needs.ts',
        at: 'needs.chat',
        call: 'here().dock.chat({ id, name }, openedHere())',
        box: 'yes',
        proof: [proof.needsChat],
    },
    // A new agent, made at the click.
    {
        file: 'console/new-agent.ts',
        at: 'newAgent',
        call: 'dock.chat({ id: campaign.id, name: campaign.name }, opened)',
        box: 'passes',
        why: passes,
    },
    { file: 'console/new-agent.ts', at: 'newAgent', call: "dock.show('start', opened)", box: 'passes', why: passes },
    {
        file: 'console/new-agent.ts',
        at: 'tryAgentAgain',
        call: 'newAgent(failed.clickId)',
        box: 'no',
        why: 'Try again: it sends a refused New agent once more, and the focus stays on the line that offered it.',
    },
    // Search.
    {
        file: 'console/palette.tsx',
        at: 'Palette › go',
        call: 'here().dock.page(parseRoute(`#/people/${encodeURIComponent(contact.id)}`))',
        box: 'none',
        why: 'A person found in Search opens their page, which has no chat.',
    },
    {
        file: 'console/palette.tsx',
        at: 'held',
        call: 'here().dock.page(parseRoute(`#/campaigns/${encodeURIComponent(campaign.id)}`), openedHere())',
        box: 'yes',
        proof: [proof.search],
    },
    {
        file: 'console/palette.tsx',
        at: 'held',
        call: 'here().dock.showChat(chat, opened)',
        box: 'yes',
        proof: [proof.search],
    },
    {
        file: 'console/palette.tsx',
        at: 'pages',
        call: 'here().dock.chat(undefined, openedHere())',
        box: 'yes',
        proof: [proof.searchPages],
    },
    {
        file: 'console/palette.tsx',
        at: 'pages',
        call: 'here().dock.showChat(held, opened)',
        box: 'yes',
        proof: [proof.searchPages],
    },
    {
        file: 'console/palette.tsx',
        at: 'pages',
        call: 'newAgent(undefined, openedHere())',
        box: 'yes',
        proof: [proof.searchAgent],
    },
    {
        file: 'console/palette.tsx',
        at: 'pages › screen',
        call: 'here().dock.page(parseRoute(href))',
        box: 'none',
        why: 'Search’s pages (People, Accounts, Performance, Work, System, Settings): none is an agent’s Chat.',
    },
    // A row's menu.
    {
        file: 'console/row-menu.tsx',
        at: 'RowMenu › choose',
        call: 'here().dock.showChat(held, opened)',
        box: 'yes',
        proof: [proof.rowMenuFresh],
    },
    {
        file: 'console/row-menu.tsx',
        at: 'RowMenu › choose',
        call: 'here().dock.showChat(held)',
        box: 'no',
        why: 'Remove conversation shows the chat to ask before removing it: the question’s Keep takes the focus, and the box is held while it stands.',
    },
    // The sidebar.
    {
        file: 'console/side.ts',
        at: 'openAgentRow',
        call: 'here().dock.chat({ id, name: row?.title ?? id }, opened)',
        box: 'yes',
        proof: [proof.agentsRow, proof.rowMenuOpen],
    },
    {
        file: 'console/side.ts',
        at: 'openRow',
        call: 'here().dock.showChat(held, opened)',
        box: 'yes',
        proof: [proof.chatsRow, proof.rowMenuOpen],
    },
    { file: 'console/side.ts', at: 'side.agent', call: 'openAgentRow(id)', box: 'yes', proof: [proof.agentsRow] },
    { file: 'console/side.ts', at: 'side.chat', call: 'openRow(id)', box: 'yes', proof: [proof.chatsRow] },
    {
        file: 'console/side.ts',
        at: 'side.feed',
        call: 'here().dock.showChat(held, opened)',
        box: 'yes',
        proof: [proof.feedRow],
    },
    {
        file: 'console/side.ts',
        at: 'side.new',
        call: 'here().dock.chat(undefined, opened)',
        box: 'yes',
        proof: [proof.plusChats],
    },
    {
        file: 'console/side.ts',
        at: 'side.page',
        call: 'here().dock.page(parseRoute(hash))',
        box: 'none',
        why: 'The sidebar’s pages (People, Accounts, Performance, Work, System, Settings): none is an agent’s Chat.',
    },
    {
        file: 'console/side.ts',
        at: 'side.show',
        call: 'here().dock.show(panel, opened)',
        box: 'yes',
        proof: [proof.mark],
    },
    {
        file: 'console/side.ts',
        at: 'side.start',
        call: 'newAgent(undefined, opened)',
        box: 'yes',
        proof: [proof.plusAgents],
    },
    // Start's send, which makes the chat and shows it.
    {
        file: 'console/start.tsx',
        at: 'begin',
        call: 'here().dock.showChat(id, opened)',
        box: 'yes',
        proof: [proof.startSend],
    },
    // The classic pages hand a chat to `chatAbout`.
    {
        file: 'screens/campaigns/panel.tsx',
        at: 'CampaignPanel › chat',
        call: 'useCampaignChat()',
        box: 'yes',
        proof: [proof.classic],
    },
    {
        file: 'screens/campaigns/screen.tsx',
        at: 'CampaignsScreen › chat',
        call: 'useCampaignChat()',
        box: 'yes',
        proof: [proof.classic],
    },
    {
        file: 'screens/people/screen.tsx',
        at: 'PeopleScreen › chat',
        call: 'useCampaignChat()',
        box: 'yes',
        proof: [proof.classic],
    },
    // The way out of Settings, by Back or Escape.
    {
        file: 'screens/settings/way-out.ts',
        at: 'leaveSettings',
        call: 'dock.page(place.route)',
        box: 'none',
        why: 'Settings opened from a page goes back to it; an agent’s Chat is a chat’s pane, which the road below shows again.',
    },
    {
        file: 'screens/settings/way-out.ts',
        at: 'leaveSettings',
        call: "dock.show('start', opened)",
        box: 'yes',
        proof: [proof.settingsBackStart],
    },
    {
        file: 'screens/settings/way-out.ts',
        at: 'leaveSettings',
        call: 'dock.show(place.pane as Parameters<typeof dock.show>[0])',
        box: 'none',
        why: 'Settings opened from a panel of no box goes back to it: the Dashboard, Needs you, the Agents page, Activity or Connect.',
    },
    {
        file: 'screens/settings/way-out.ts',
        at: 'leaveSettings',
        call: "dock.showChat(place.pane.slice('chat:'.length), opened)",
        box: 'yes',
        proof: [proof.settingsBackChat],
    },
];

const sourceDir = join(import.meta.dirname, '..');
const screenTests = readFileSync(join(sourceDir, 'console', 'box-focus.screen.test.tsx'), 'utf8');

/** The dock's moves, by the method's name. */
const dockMoves = new Set(['chat', 'showChat', 'show', 'page']);
/** The console's own functions that show a chat or Start, or reach the dock for a person's act. */
const roads = new Set(['newAgent', 'openRow', 'openAgentRow', 'useCampaignChat', 'showAgentChat', 'reveal']);

/** A string literal's text, else null. */
const literal = (node: ts.Node | undefined): string | null =>
    node !== undefined && (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) ? node.text : null;

/** Whether the call can show a chat or Start, so the table must list it. */
function showsAChat(call: ts.CallExpression): boolean {
    const callee = call.expression;
    const first = call.arguments[0];
    if (ts.isPropertyAccessExpression(callee) && dockMoves.has(callee.name.text)) {
        const owner = callee.expression;
        const isDock =
            (ts.isIdentifier(owner) && owner.text === 'dock') ||
            (ts.isPropertyAccessExpression(owner) && owner.name.text === 'dock');
        if (!isDock) return false;
        // A panel named by its name is one of no box; only Start has one.
        if (callee.name.text === 'show') return literal(first) === null || literal(first) === 'start';
        // A page named by a literal screen other than an agent's is a page of no chat.
        if (callee.name.text === 'page' && first !== undefined && ts.isObjectLiteralExpression(first)) {
            const screen = first.properties.find(
                (each) => ts.isPropertyAssignment(each) && ts.isIdentifier(each.name) && each.name.text === 'screen',
            );
            const named = screen && ts.isPropertyAssignment(screen) ? literal(screen.initializer) : null;
            return named === null || named === 'campaigns';
        }
        return true;
    }
    if (ts.isIdentifier(callee) && roads.has(callee.text)) {
        if (callee.text === 'reveal') return literal(first) === null || literal(first) === 'start';
        return true;
    }
    // The address of an agent's Chat put in the bar by the page itself shows that chat.
    if (ts.isIdentifier(callee) && (callee.text === 'navigate' || callee.text === 'replace'))
        return (
            first !== undefined &&
            ts.isCallExpression(first) &&
            ts.isIdentifier(first.expression) &&
            first.expression.text === 'agentChatRoute'
        );
    return false;
}

/** Where a call stands: the named places around it, outermost first (a function, a variable, a property, an action). */
function placeOf(node: ts.Node): string {
    const names: string[] = [];
    for (let at = node.parent; at !== undefined; at = at.parent) {
        if (ts.isFunctionDeclaration(at) && at.name) names.push(at.name.text);
        else if (ts.isVariableDeclaration(at) && ts.isIdentifier(at.name)) names.push(at.name.text);
        else if (ts.isPropertyAssignment(at) && ts.isIdentifier(at.name)) names.push(at.name.text);
        else if (ts.isMethodDeclaration(at) && ts.isIdentifier(at.name)) names.push(at.name.text);
        else if (
            ts.isCallExpression(at) &&
            ts.isIdentifier(at.expression) &&
            at.expression.text === 'define' &&
            literal(at.arguments[0]) !== null
        )
            names.push(literal(at.arguments[0])!);
    }
    return names.reverse().join(' › ');
}

interface Found {
    file: string;
    at: string;
    call: string;
    /** How many arguments the call passes: a dock's move passes the person's act as its second. */
    args: number;
    dock: boolean;
}

/** Every call in the page's source, its tests and test helpers aside, that can show a chat or Start. */
function found(): Found[] {
    const files = readdirSync(sourceDir, { recursive: true, encoding: 'utf8' })
        .map((name) => name.split(sep).join('/'))
        .filter((name) => /\.tsx?$/.test(name) && !/\.test\.tsx?$/.test(name) && !name.startsWith('test/'));
    const calls: Found[] = [];
    for (const file of files) {
        const text = readFileSync(join(sourceDir, file), 'utf8');
        const source = ts.createSourceFile(
            file,
            text,
            ts.ScriptTarget.Latest,
            true,
            file.endsWith('x') ? ts.ScriptKind.TSX : ts.ScriptKind.TS,
        );
        const visit = (node: ts.Node): void => {
            if (ts.isCallExpression(node) && showsAChat(node))
                calls.push({
                    file,
                    at: placeOf(node),
                    call: node.getText(source).replace(/\s+/g, ' '),
                    args: node.arguments.length,
                    dock: ts.isPropertyAccessExpression(node.expression),
                });
            ts.forEachChild(node, visit);
        };
        visit(source);
    }
    return calls;
}

const key = (each: { file: string; at: string; call: string }) => `${each.file} | ${each.at} | ${each.call}`;

describe('every act that shows a chat', () => {
    const calls = found();
    const listed = new Set(rows.map(key));

    it('is found in the source, the roads already known among them', () => {
        expect(calls.map(key)).toEqual(
            expect.arrayContaining([
                expect.stringMatching(/^console\/side\.ts \| side\.new \| here\(\)\.dock\.chat\(undefined, opened\)$/),
            ]),
        );
    });

    it('has a row in the table, and every row a call in the source', () => {
        const inSource = new Set(calls.map(key));
        expect([...inSource].filter((each) => !listed.has(each)).sort()).toEqual([]);
        expect([...listed].filter((each) => !inSource.has(each)).sort()).toEqual([]);
    });

    it('passes the person’s act to the dock where its row says the box takes the focus, and none where it says not', () => {
        const box = new Map(rows.map((row) => [key(row), row.box]));
        const wrong = calls
            .filter((each) => each.dock)
            .filter((each) => {
                const said = box.get(key(each));
                return (said === 'yes' && each.args < 2) || (said === 'no' && each.args >= 2);
            })
            .map(key);
        expect(wrong).toEqual([]);
    });

    it('is proved, where the box takes the focus, by a screen test that exists', () => {
        const unproved = rows
            .filter((row): row is Extract<Row, { box: 'yes' }> => row.box === 'yes')
            .flatMap((row) =>
                row.proof.filter((name) => !screenTests.includes(`'${name}'`)).map((name) => `${key(row)}: ${name}`),
            );
        expect(unproved).toEqual([]);
    });

    it('says why, where the box does not take the focus', () => {
        expect(rows.filter((row) => row.box !== 'yes' && row.why.trim() === '').map(key)).toEqual([]);
    });
});
