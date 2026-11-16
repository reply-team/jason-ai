import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../../api/session';
import { Console } from '../../console/console';
import * as consoleCampaigns from '../../console/campaigns';
import { attention } from '../../console/needs';
import { chats, conversations } from '../../console/state';
import { mode } from '../../platform/mode';
import { activatePlugins } from '../../platform/plugin';
import { browserPlugins } from '../../plugins';
import { route } from '../../router';
import { inFront } from '../../test/area';
import { stubDesktop, stubStream, type StubDesktop } from '../../test/desktop';
import { FakeApi, watch } from '../../test/fake-api';
import type { FixtureName } from '../../test/fixtures';
import { nothingWaitsInChats } from '../../test/waiting';
import * as store from './store';

// An agent's page in the console's look: one sheet of the console's own, which draws the browser shell's agent page as
// the console draws an agent - the agent alone in the area, its name, state and type in a bar with its moves at the
// right, the tabs under it, its own Settings as cards of rows. Look alone, but for one rule: while an agent is open the
// list beside it is not drawn, but for what the screen says in it, and Close gives it back. Everything else reaches the
// agent's own panel alone: what is drawn in its place (an approval, a question, a person) and the dialogs its moves open
// keep their own look. Read from the file: the test run hands a stylesheet import back empty, and it starts in ui/.

const sheetFile = 'src/console/agent.css';
const sheet = readFileSync(sheetFile, 'utf8').replace(/[/][*][^]*?[*][/]/g, '');
const rules = Array.from(sheet.matchAll(/([^{}]+)[{]([^{}]*)[}]/g), (rule) => ({
    // A rule inside a media query is matched past the query's own brace, so its selectors are the rule's alone.
    selectors: rule[1]!.split(',').map((each) => each.replace(/\s+/g, ' ').trim()),
    body: rule[2]!.replace(/\s+/g, ' ').trim(),
}));
const body = (selector: string) =>
    rules
        .filter((rule) => rule.selectors.includes(selector))
        .map((rule) => rule.body)
        .join(' ');
const page = '.jc-console .harness .main.campaigns:not(.single)';
const agent = `${page} > .panel[data-panel='agent']`;
const darkRoots = [`:root[data-theme='dark'] ${agent}`, `:root:not([data-theme='light']) ${agent}`];
const hiding = `${page} > .list > :not(.notice)`;

/** Every selector of the sheet that an element matches. */
const reached = (element: Element) =>
    rules.flatMap((rule) => rule.selectors).filter((selector) => element.matches(selector));
/** The sheet's hiding selectors that take an element out of view, itself or through a parent. */
const hiddenBy = (element: Element) =>
    rules
        .filter((rule) => /display: none/.test(rule.body))
        .flatMap((rule) => rule.selectors)
        .filter((selector) => element.closest(selector) !== null);

function files(dir: string): string[] {
    return readdirSync(dir).flatMap((name) => {
        const path = join(dir, name);
        return statSync(path).isDirectory() ? files(path) : [path];
    });
}

describe('the sheet of an agent’s page in the console', () => {
    it('reaches the open agent’s own panel, and of the page around it only its ground and the list beside it', () => {
        expect(rules.length).toBeGreaterThan(0);
        const around = [page, `${page} > .list`, hiding, `${page} > .list > .notice`];
        for (const rule of rules)
            for (const selector of rule.selectors)
                expect(
                    around.includes(selector) ||
                        [agent, ...darkRoots].some((root) => selector === root || selector.startsWith(`${root} `)),
                    selector,
                ).toBe(true);
    });

    it('is loaded by the console mount alone, so the classic shell never receives it', () => {
        const users = files('src')
            .filter((file) => /\.(tsx?|css)$/.test(file) && !file.endsWith('.test.tsx'))
            .filter((file) => readFileSync(file, 'utf8').includes('agent.css'))
            .map((file) => file.split('\\').join('/'));
        expect(users).toEqual(['src/console/mount.tsx']);
        const mount = readFileSync('src/console/mount.tsx', 'utf8');
        const sheets = Array.from(mount.matchAll(/^import '(\.\/[\w-]+\.css)';\r?$/gm), (each) => each[1]);
        // After the shell's own sheet, so that its rules are the word on the agent's page.
        expect(sheets.indexOf('./harness.css')).toBeGreaterThan(-1);
        expect(sheets.indexOf('./agent.css')).toBeGreaterThan(sheets.indexOf('./harness.css'));
    });

    // Look alone: nothing of the page is cut or replaced by words of the sheet's; the one thing it does not draw is the
    // list beside an open agent, and of that list what the screen says is kept.
    it('writes nothing, and hides only the list beside an open agent, its notices kept', () => {
        for (const rule of rules) {
            expect(rule.body, rule.selectors.join(', ')).not.toMatch(
                /visibility: hidden|(^|; )content:|-webkit-line-clamp|text-overflow/,
            );
            if (/display: none/.test(rule.body)) expect(rule.selectors).toEqual([hiding]);
        }
        expect(body(hiding)).toBe('display: none;');
        // The list itself is drawn at every width, against the shell's narrow rule that takes the whole of it out.
        expect(body(`${page} > .list`)).toMatch(/display: flex;/);
        // What the screen says stands above the bar at the bar's margin, from the list and from the agent's own panel.
        const notice = body(`${page} > .list > .notice`);
        expect(notice).toMatch(/margin: 14px 28px 0;/);
        expect(body(`${agent} > .notice`)).toBe(notice);
        expect(body(page)).toMatch(/grid-template-columns: minmax\(0, 1fr\)/);
        expect(body(page)).toMatch(/background: var\(--panel\)/);
        // What is left of the list takes its notices' height and no share of the page's.
        expect(body(page)).toMatch(/align-content: start;/);
    });

    it('draws the header as the console’s bar: the name and its pills at the left, the moves and Close at the right', () => {
        expect(body(agent)).toMatch(/display: grid; grid-template-columns: minmax\(0, 1fr\) auto auto;/);
        expect(body(agent)).toMatch(/grid-auto-flow: row dense/);
        expect(body(`${agent} > .panel-header`)).toBe('display: contents;');
        expect(body(`${agent} > .panel-header > .heading`)).toMatch(/grid-column: 1;/);
        expect(body(`${agent} > .panel-actions`)).toMatch(/grid-column: 2;/);
        expect(body(`${agent} > .panel-header > .close`)).toMatch(/grid-column: 3;/);
        expect(body(`${agent} > .panel-header h2`)).toMatch(/font-size: 17px; font-weight: 650;/);
        expect(body(`${agent} > .panel-header > .close`)).toMatch(/width: 28px; height: 28px;/);
    });

    it('draws the state and the type as the console’s pills, each state its own', () => {
        const pill = body(`${agent} > .panel-header .sub .badge`);
        expect(pill).toMatch(
            /border: 1px solid var\(--line\); border-radius: 999px; padding: 1px 9px; font-size: 12px;/,
        );
        expect(body(`${agent} > .panel-header .badge[data-status]`)).toMatch(/text-transform: capitalize/);
        expect(body(`${agent} > .panel-header .badge[data-status='active']`)).toMatch(
            /color: var\(--green\); background: var\(--green-2\); border-color: transparent/,
        );
        expect(body(`${agent} > .panel-header .badge[data-status='paused']`)).toMatch(
            /color: var\(--amber\); background: var\(--amber-2\); border-color: transparent/,
        );
        expect(body(`${agent} > .panel-header .badge.kind[data-kind='sequence']`)).toMatch(
            /color: var\(--blue\); background: var\(--blue-2\); border-color: transparent/,
        );
        expect(body(`${agent} > .panel-header .badge.kind[data-kind='adaptive']`)).toMatch(
            /color: var\(--violet\); background: var\(--violet-2\); border-color: transparent/,
        );
    });

    it('makes Start and Resume the primary move and leaves Pause plain', () => {
        const primary = /background: var\(--teal\); border-color: var\(--teal\); color: var\(--on-teal\)/;
        expect(body(`${agent} > .panel-actions > .btn.agent-move-start`)).toMatch(primary);
        expect(body(`${agent} > .panel-actions > .btn.agent-move-resume`)).toMatch(primary);
        expect(body(`${agent} > .panel-actions > .btn.agent-move-pause`)).toBe('');
        expect(body(`${agent} > .panel-actions > .btn`)).toMatch(/border-radius: 9px;/);
    });

    it('draws the tabs as the console’s: a row with a teal underline under the one shown', () => {
        expect(body(`${agent} > .campaign-tabs`)).toMatch(/gap: 22px; padding: 0 28px;/);
        expect(body(`${agent} > .campaign-tabs .tabs a.active`)).toMatch(
            /color: var\(--teal\); border-bottom-color: var\(--teal\)/,
        );
        // The timeline's chips and rows sit at the bar's margin: 12px of the page's and 16px of their own.
        expect(body(`${agent} > .timeline`)).toMatch(/padding: 4px 12px 0;/);
        expect(body(`${agent} > .timeline > .chips`)).toMatch(/padding-left: 16px; padding-right: 16px;/);
    });

    it('draws the agent’s own Settings as cards of rows, as the console’s Settings draws a fact', () => {
        for (const part of ['.facts', '.section']) {
            const card = body(`${agent} > .panel-body > ${part}`);
            expect(card).toMatch(/border-radius: 16px/);
            expect(card).toMatch(/box-shadow: 0 1px 2px rgba\(15, 23, 42, 0.03\)/);
            expect(card).toMatch(/background: var\(--panel\)/);
        }
        expect(body(`${agent} .send-mode-row`)).toMatch(
            /grid-template-columns: minmax\(0, 1.3fr\) minmax\(0, 2fr\) 170px/,
        );
        expect(body(`${agent} .facts dt`)).toMatch(/padding: 11px 0;/);
        // The archive's card sets its button a step below its sentence.
        expect(body(`${agent} > .panel-body > .section.archive-agent .btn-row`)).toMatch(/margin-top: 12px/);
        expect(body(`${agent} .facts dd`)).toMatch(/text-align: right; overflow-wrap: anywhere;/);
        // A narrow window stacks a fact: its label over its value, the value to the left.
        expect(sheet).toMatch(
            /@media \(max-width: 820px\) \{[^@]*\.facts \{\s*grid-template-columns: minmax\(0, 1fr\);[^@]*\.facts dd \{\s*padding-left: 16px;\s*text-align: left;/,
        );
        for (const root of darkRoots) expect(body(`${root} .facts dt`)).toMatch(/color: var\(--mute\)/);
    });
});

/** The reads of an agent's page in the console, each answered whatever it asks: the page's look is what is held here. */
function agentApi(get: (id: string) => FixtureName, people: FixtureName = 'campaign.list_contacts/summary-waiting') {
    return nothingWaitsInChats(new FakeApi())
        .answerAnyRequest('approval.list', 'approval.list/empty')
        .answerAnyRequest('decision.list', 'decision.list/empty')
        .answerAnyRequest('preference.list', 'preference.list/set')
        .answerAnyRequest('campaign.list', 'campaign.list/screen')
        .answerAnyRequest('campaign.summary', 'campaign.summary/all')
        .answerAnyRequest('campaign.get', (body) => get((body as { campaign_id: string }).campaign_id))
        .answerAnyRequest('campaign.list_contacts', people)
        .answerAnyRequest('workitem.list', 'workitem.list/creating-none')
        .answerAnyRequest('journal.list', 'journal.list/by-campaign');
}

function renderConsole(fake: FakeApi): StubDesktop {
    const desktop = stubDesktop();
    watch(fake);
    activatePlugins(browserPlugins);
    localStorage.clear();
    location.hash = '';
    route.value = { screen: 'attention' };
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    fake.answer('system.info', 'system.info/with-ui');
    render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
    return desktop;
}

/** The agent's header once its page is drawn under the name given. */
async function headerOf(name: string): Promise<HTMLElement> {
    return waitFor(() => {
        const found = document.querySelector<HTMLElement>('.harness .main.campaigns .panel-header');
        expect(found?.querySelector('h2')?.textContent).toBe(name);
        return found!;
    });
}

/** The agents list drawn again, with a row of it in sight. */
async function listDrawn(): Promise<void> {
    await waitFor(() => {
        const list = document.querySelector<HTMLElement>('.harness .main.campaigns.single > .list');
        expect(list).not.toBeNull();
        const spring = within(list!).getByText('Spring');
        expect(hiddenBy(spring)).toEqual([]);
    });
}

const opened = async (desktop: StubDesktop, id: string, name: string) => {
    await screen.findByRole('heading', { name: 'Who do you want to reach?' });
    desktop.link({ page: 'campaign', id });
    return headerOf(name);
};

/** The agent's People tab, chosen in its header. */
const peopleTab = (header: HTMLElement) =>
    fireEvent.click(within(header.closest<HTMLElement>('.panel')!).getByRole('tab', { name: 'People' }));

/** A person's row on the People tab, by their name. */
const row = async (name: string) => (await screen.findByText(name)).closest('.row') as HTMLElement;

describe('an agent’s page drawn in the console', () => {
    beforeEach(() => {
        chats.value = {};
        conversations.value = [];
        attention.loaded.value = false;
        attention.approvals.value = [];
        attention.questions.value = [];
        attention.detail.value = null;
        consoleCampaigns.campaigns.value = [];
        consoleCampaigns.read.value = false;
        store.shown.value = null;
        store.waiting.value = null;
        store.actionFailure.value = null;
        mode.value = 'live';
    });
    afterEach(() => cleanup());

    it('says its state and its move by attributes the sheet reads, and Close from a deep link leads to the list', async () => {
        const desktop = renderConsole(agentApi(() => 'campaign.get/details'));
        const header = await opened(desktop, 'cmp_1', 'Spring');
        expect(header.closest('.panel')?.getAttribute('data-panel')).toBe('agent');
        expect(header.querySelector('.badge[data-status]')?.getAttribute('data-status')).toBe('active');
        expect(header.querySelector('.badge[data-status]')?.textContent).toBe('active');
        expect(screen.getByRole('button', { name: 'Pause' }).className).toBe('btn agent-move-pause');
        // A deep link opens the page as an open agent, the one state where the sheet leaves the list out; Close leads to
        // the list, drawn again, and not to nowhere.
        expect(header.closest('.main.campaigns')!.classList.contains('single')).toBe(false);
        fireEvent.click(within(header).getByRole('button', { name: 'Close' }));
        await waitFor(() => expect(route.value).toMatchObject({ screen: 'campaigns' }));
        expect(route.value).not.toHaveProperty('id');
        await listDrawn();
    });

    it('says a read that failed while an agent is open, out of the list’s hiding', async () => {
        const fake = agentApi(() => 'campaign.get/details');
        fake.unreachable('approval.get');
        const desktop = renderConsole(fake);
        peopleTab(await opened(desktop, 'cmp_1', 'Spring'));
        fireEvent.click(await row('Ada Example'));
        const alert = await screen.findByRole('alert');
        expect(alert.closest('.main.campaigns:not(.single) > .list')).not.toBeNull();
        expect(hiddenBy(alert)).toEqual([]);
        expect(reached(alert.parentElement!)).toEqual([`${page} > .list`]);
    });

    it.each([
        { move: 'Pause', get: 'campaign.get/details' },
        { move: 'Start', get: 'campaign.get/send-mode-override' },
    ] as const)('leaves the $move dialog in its own look', async ({ move, get }) => {
        const desktop = renderConsole(agentApi(() => get));
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        desktop.link({ page: 'campaign', id: 'cmp_1' });
        fireEvent.click(await screen.findByRole('button', { name: move }));
        const dialog = await screen.findByRole('dialog');
        const buttons = within(dialog).getAllByRole('button');
        expect(buttons.length).toBeGreaterThan(1);
        for (const each of [dialog, ...buttons]) expect(reached(each), each.textContent ?? '').toEqual([]);
    });

    it('leaves an approval opened in the agent’s place in its own look', async () => {
        const fake = agentApi(() => 'campaign.get/details').answerAnyRequest(
            'approval.get',
            'approval.get/for-campaign-row',
        );
        const desktop = renderConsole(fake);
        peopleTab(await opened(desktop, 'cmp_1', 'Spring'));
        fireEvent.click(await row('Ada Example'));
        const approve = await screen.findByRole('button', { name: 'Approve' });
        const panel = approve.closest<HTMLElement>('.panel')!;
        const touched = [
            panel,
            panel.querySelector('.panel-header')!,
            approve,
            within(panel).getByRole('button', { name: 'Reject' }),
        ];
        for (const each of touched) expect(reached(each), each.className).toEqual([]);
    });

    it('leaves a person opened in the agent’s place in its own look', async () => {
        const fake = agentApi(() => 'campaign.get/details', 'campaign.list_contacts/summary-failed')
            .answerAnyRequest('contact.get', 'contact.get/imported')
            .answerAnyRequest('suppression.list', 'suppression.list/grace-email-clear');
        const desktop = renderConsole(fake);
        peopleTab(await opened(desktop, 'cmp_1', 'Spring'));
        fireEvent.click(await row('Cleo Example'));
        const person = await waitFor(() => {
            const shown = document.querySelector<HTMLElement>('.person-panel');
            expect(shown?.querySelector('.panel-header h2')).toBeTruthy();
            return shown!;
        });
        const touched = [person, person.querySelector('.panel-header')!, ...within(person).getAllByRole('button')];
        for (const each of touched) expect(reached(each), each.className).toEqual([]);
    });

    it('opened from the list, gives the list back to the browser’s Back', async () => {
        const desktop = renderConsole(agentApi(() => 'campaign.get/details'));
        const first = await opened(desktop, 'cmp_1', 'Spring');
        fireEvent.click(within(first).getByRole('button', { name: 'Close' }));
        await listDrawn();
        fireEvent.click(
            within(document.querySelector<HTMLElement>('.harness .main.campaigns > .list')!).getByText('Spring'),
        );
        await headerOf('Spring');
        history.back();
        await listDrawn();
    });

    it('from one agent to another by the palette shows the other’s header, and the sidebar’s entry still opens its panel', async () => {
        const fake = agentApi((id) => (id === 'cmp_2' ? 'campaign.get/unlinked' : 'campaign.get/details'));
        const desktop = renderConsole(fake);
        await opened(desktop, 'cmp_1', 'Spring');
        fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
        fireEvent.change(await screen.findByPlaceholderText('Search agents, people, pages and settings'), {
            target: { value: 'autumn' },
        });
        fireEvent.click(within(await screen.findByRole('listbox')).getAllByRole('option')[0]!);
        await headerOf('Autumn');
        fireEvent.click(
            within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: /^Needs you/ }),
        );
        await waitFor(() => expect(inFront()).toBe('Needs you'));
    });

    it('opened by two links in a row shows the second, and Close leads to the list', async () => {
        const fake = agentApi((id) => (id === 'cmp_2' ? 'campaign.get/unlinked' : 'campaign.get/details'));
        const desktop = renderConsole(fake);
        await opened(desktop, 'cmp_1', 'Spring');
        desktop.link({ page: 'campaign', id: 'cmp_2' });
        const header = await headerOf('Autumn');
        fireEvent.click(within(header).getByRole('button', { name: 'Close' }));
        await listDrawn();
    });
});
