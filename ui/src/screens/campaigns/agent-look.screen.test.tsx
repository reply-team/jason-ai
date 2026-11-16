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
import { stubDesktop, stubStream } from '../../test/desktop';
import { FakeApi, watch } from '../../test/fake-api';
import * as store from './store';

// An agent's page in the console's look: one sheet of the console's own, which draws the browser shell's agent page as
// the console draws an agent - the agent alone in the area, its name, state and type in a bar with its moves at the
// right, the tabs under it, its own Settings as cards of rows. Look alone, but for one rule: while an agent is open the
// list beside it is not drawn, and Close gives it back. Read from the file: the test run hands a stylesheet import back
// empty, and it starts in ui/.

const sheetFile = 'src/console/agent.css';
const sheet = readFileSync(sheetFile, 'utf8').replace(/[/][*][^]*?[*][/]/g, '');
const rules = Array.from(sheet.matchAll(/([^{}]+)[{]([^{}]*)[}]/g), (rule) => ({
    // A rule inside a media query is matched past the query's own brace, so its selectors are the rule's alone.
    selectors: rule[1]!.split(',').map((each) => each.trim()),
    body: rule[2]!.replace(/\s+/g, ' ').trim(),
}));
const body = (selector: string) =>
    rules
        .filter((rule) => rule.selectors.includes(selector))
        .map((rule) => rule.body)
        .join(' ');
const scope = '.jc-console .harness .main.campaigns';
const open = `${scope}:not(.single)`;
const dark = `:root[data-theme='dark'] ${scope}`;
const darkAuto = `:root:not([data-theme='light']) ${scope}`;

function files(dir: string): string[] {
    return readdirSync(dir).flatMap((name) => {
        const path = join(dir, name);
        return statSync(path).isDirectory() ? files(path) : [path];
    });
}

describe('the sheet of an agent’s page in the console', () => {
    it('reaches the agents’ page in the console and nothing else', () => {
        expect(rules.length).toBeGreaterThan(0);
        for (const rule of rules)
            for (const selector of rule.selectors)
                expect(
                    [scope, dark, darkAuto].some((root) => selector === root || selector.startsWith(root)),
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
    // list beside an open agent.
    it('writes nothing, and hides only the list beside an open agent', () => {
        for (const rule of rules) {
            expect(rule.body, rule.selectors.join(', ')).not.toMatch(
                /visibility: hidden|(^|; )content:|-webkit-line-clamp|text-overflow/,
            );
            if (/display: none/.test(rule.body)) expect(rule.selectors).toEqual([`${open} > .list`]);
        }
        expect(body(`${open} > .list`)).toBe('display: none;');
        expect(body(open)).toMatch(/grid-template-columns: minmax\(0, 1fr\)/);
        expect(body(open)).toMatch(/background: var\(--panel\)/);
    });

    it('draws the header as the console’s bar: the name and its pills at the left, the moves and Close at the right', () => {
        expect(body(`${open} > .panel`)).toMatch(/display: grid; grid-template-columns: minmax\(0, 1fr\) auto auto;/);
        expect(body(`${open} > .panel`)).toMatch(/grid-auto-flow: row dense/);
        expect(body(`${open} > .panel > .panel-header`)).toBe('display: contents;');
        expect(body(`${open} > .panel .panel-header .heading`)).toMatch(/grid-column: 1;/);
        expect(body(`${open} > .panel > .panel-actions`)).toMatch(/grid-column: 2;/);
        expect(body(`${open} > .panel .panel-header .close`)).toMatch(/grid-column: 3;/);
        expect(body(`${open} > .panel .panel-header h2`)).toMatch(/font-size: 17px; font-weight: 650;/);
        expect(body(`${open} > .panel .panel-header .close`)).toMatch(/width: 28px; height: 28px;/);
    });

    it('draws the state and the type as the console’s pills, each state its own', () => {
        const pill = body(`${scope} .panel-header .sub .badge`);
        expect(pill).toMatch(
            /border: 1px solid var\(--line\); border-radius: 999px; padding: 1px 9px; font-size: 12px;/,
        );
        expect(body(`${scope} .panel-header .badge[data-status]`)).toMatch(/text-transform: capitalize/);
        expect(body(`${scope} .panel-header .badge[data-status='active']`)).toMatch(
            /color: var\(--green\); background: var\(--green-2\); border-color: transparent/,
        );
        expect(body(`${scope} .panel-header .badge[data-status='paused']`)).toMatch(
            /color: var\(--amber\); background: var\(--amber-2\); border-color: transparent/,
        );
        expect(body(`${scope} .panel-header .badge.kind[data-kind='sequence']`)).toMatch(
            /color: var\(--blue\); background: var\(--blue-2\); border-color: transparent/,
        );
        expect(body(`${scope} .panel-header .badge.kind[data-kind='adaptive']`)).toMatch(
            /color: var\(--violet\); background: var\(--violet-2\); border-color: transparent/,
        );
    });

    it('makes Start and Resume the primary move and leaves Pause plain', () => {
        const primary = /background: var\(--teal\); border-color: var\(--teal\); color: #fff/;
        expect(body(`${scope} .panel-actions .btn.agent-move-start`)).toMatch(primary);
        expect(body(`${scope} .panel-actions .btn.agent-move-resume`)).toMatch(primary);
        expect(body(`${scope} .panel-actions .btn.agent-move-pause`)).toBe('');
        expect(body(`${scope} .panel-actions .btn`)).toMatch(/border-radius: 9px;/);
    });

    it('draws the tabs as the console’s: a row with a teal underline under the one shown', () => {
        expect(body(`${scope} .campaign-tabs`)).toMatch(/gap: 22px; padding: 0 28px;/);
        expect(body(`${scope} .campaign-tabs .tabs a.active`)).toMatch(
            /color: var\(--teal\); border-bottom-color: var\(--teal\)/,
        );
    });

    it('draws the agent’s own Settings as cards of rows', () => {
        for (const part of ['.facts', '.section']) {
            const card = body(`${scope} .panel-body > ${part}`);
            expect(card).toMatch(/border-radius: 16px/);
            expect(card).toMatch(/box-shadow: 0 1px 2px rgba\(15, 23, 42, 0.03\)/);
            expect(card).toMatch(/background: var\(--panel\)/);
        }
        expect(body(`${scope} .send-mode-row`)).toMatch(
            /grid-template-columns: minmax\(0, 1.3fr\) minmax\(0, 2fr\) 170px/,
        );
        expect(body(`${scope} .facts dd`)).toMatch(/text-align: right/);
        for (const root of [dark, darkAuto]) expect(body(`${root} .facts dt`)).toMatch(/color: var\(--mute\)/);
    });
});

describe('an agent’s page drawn in the console', () => {
    beforeEach(() => {
        chats.value = {};
        conversations.value = [];
        attention.detail.value = null;
        consoleCampaigns.campaigns.value = [];
        consoleCampaigns.read.value = false;
        store.shown.value = null;
        mode.value = 'live';
    });
    afterEach(() => cleanup());

    it('says its state and its move by attributes the sheet reads, and nothing else changes', async () => {
        const desktop = stubDesktop();
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending', 'approval.list/by-campaign-pending')
            .answer('approval.get', 'approval.get/pending-with-preview', 'approval.get/other-pending')
            .answer('decision.list', 'decision.list/pending', 'decision.list/by-campaign-pending')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all', 'campaign.summary/some')
            .answer('campaign.get', 'campaign.get/details')
            .answer('workitem.list', 'workitem.list/by-campaign-planned', 'workitem.list/by-campaign-running')
            .answer('journal.list', 'journal.list/by-campaign');
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
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        desktop.link({ page: 'campaign', id: 'cmp_1' });
        const header = await waitFor(() => {
            const found = document.querySelector<HTMLElement>('.harness .main.campaigns .panel-header');
            expect(found).not.toBeNull();
            return found!;
        });
        expect(header.querySelector('.badge[data-status]')?.getAttribute('data-status')).toBe('active');
        expect(header.querySelector('.badge[data-status]')?.textContent).toBe('active');
        const pause = screen.getByRole('button', { name: 'Pause' });
        expect(pause.className).toBe('btn agent-move-pause');
        // A deep link opens the page as an open agent, the one state where the sheet leaves the list out; Close leads to
        // the list, drawn again, and not to nowhere.
        const main = header.closest<HTMLElement>('.main.campaigns')!;
        expect(main.classList.contains('single')).toBe(false);
        fireEvent.click(within(header).getByRole('button', { name: 'Close' }));
        await waitFor(() => expect(route.value).toMatchObject({ screen: 'campaigns' }));
        expect(route.value).not.toHaveProperty('id');
        await waitFor(() => {
            const list = document.querySelector<HTMLElement>('.harness .main.campaigns.single > .list');
            expect(list).not.toBeNull();
            within(list!).getByText('Spring');
        });
    });
});
