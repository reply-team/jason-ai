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

// Settings in the console's look: one sheet of the console's own, which draws the browser shell's Settings screen as the
// console draws its pages - each section a card, the theme a segmented control, a field a row, a fact a row of its
// card. It is look alone: the screen's markup, words, controls and reads are what they were, and nothing outside
// Settings in the console is reached. Read from the file: the test run hands a stylesheet import back empty, and it
// starts in ui/.

const sheetFile = 'src/console/settings.css';
const sheet = readFileSync(sheetFile, 'utf8').replace(/[/][*][^]*?[*][/]/g, '');
const rules = Array.from(sheet.matchAll(/([^{}]+)[{]([^{}]*)[}]/g), (rule) => ({
    selectors: rule[1]!.split(',').map((each) => each.trim()),
    body: rule[2]!.replace(/\s+/g, ' ').trim(),
}));
const body = (selector: string) =>
    rules
        .filter((rule) => rule.selectors.includes(selector))
        .map((rule) => rule.body)
        .join(' ');
const scope = '.jc-console .harness .settings';
// The exact selectors the two panes add: the rule that lets the bar stay, the rules on the chosen page, and the one that hides.
const contentRule = '.jc-console .harness .content:has(> .settings[data-chosen])';
const hiding = ['appearance', 'voice', 'modes', 'notifications', 'models', 'runtime'].map(
    (name) => `${scope}[data-chosen='${name}'] > [data-section]:not([data-section='${name}'])`,
);
const panes = [
    `${scope}[data-chosen]`,
    `${scope}[data-chosen] > h1`,
    `${scope}[data-chosen] > [data-section]`,
    ...['input', 'select', 'textarea', 'button'].map((each) => `${scope}[data-chosen] > [data-section] ${each}`),
];
const dark = `:root[data-theme='dark'] ${scope}`;
const darkAuto = `:root:not([data-theme='light']) ${scope}`;

function files(dir: string): string[] {
    return readdirSync(dir).flatMap((name) => {
        const path = join(dir, name);
        return statSync(path).isDirectory() ? files(path) : [path];
    });
}

describe('the sheet of Settings in the console', () => {
    it('reaches Settings in the console and nothing else', () => {
        expect(rules.length).toBeGreaterThan(0);
        for (const rule of rules)
            for (const selector of rule.selectors)
                expect(
                    selector.startsWith(`${scope} `) ||
                        selector === contentRule ||
                        hiding.includes(selector) ||
                        panes.includes(selector) ||
                        selector === scope ||
                        selector.startsWith(`${dark} `) ||
                        selector.startsWith(`${darkAuto} `),
                    selector,
                ).toBe(true);
    });

    it('is loaded by the console mount alone, so the classic shell never receives it', () => {
        const users = files('src')
            .filter((file) => /\.(tsx?|css)$/.test(file) && !file.endsWith('.test.tsx'))
            .filter((file) => readFileSync(file, 'utf8').includes('settings.css'))
            .map((file) => file.split('\\').join('/'));
        expect(users).toEqual(['src/console/mount.tsx']);
        const mount = readFileSync('src/console/mount.tsx', 'utf8');
        // The last sheet the mount loads, after the console's palette, so that its rules are the last word on Settings.
        const sheets = Array.from(mount.matchAll(/^import '(\.\/[\w-]+\.css)';\r?$/gm), (each) => each[1]);
        expect(sheets.length).toBeGreaterThan(1);
        expect(sheets.at(-1)).toBe('./settings.css');
    });

    // Look alone: nothing of the screen is hidden, cut or replaced by words of the sheet's.
    it('hides, cuts and writes nothing', () => {
        // The one exception is the two panes' rule, which hides the sections not chosen (settings-panes.screen.test.tsx).
        for (const rule of rules.filter((each) => each.selectors.join() !== hiding.join()))
            expect(rule.body, rule.selectors.join(', ')).not.toMatch(
                /display: none|visibility: hidden|content:|-webkit-line-clamp|text-overflow/,
            );
    });

    it('draws the page as the console draws its pages: a bar for the title, each section a card', () => {
        expect(body(`${scope} > h1`)).toMatch(/font-size: 17px; font-weight: 650;/);
        expect(body(`${scope} > h1`)).toMatch(/border-bottom: 1px solid var\(--line\)/);
        const card = body(`${scope} > section`);
        expect(card).toMatch(/border-radius: 16px/);
        expect(card).toMatch(/box-shadow: 0 1px 2px rgba\(15, 23, 42, 0.03\)/);
        expect(card).toMatch(/background: var\(--panel\)/);
        expect(body(`${scope} > [data-section='runtime'] > .card`)).toBe(card);
        expect(body(`${scope} h2`)).toMatch(/font-size: 15px; font-weight: 650;/);
    });

    it('draws the theme as a segmented control, a field as a setting row, a fact as a row of its card', () => {
        expect(body(`${scope} .choices`)).toMatch(/background: var\(--seg-track\)/);
        expect(body(`${scope} .choices button[aria-pressed='true']`)).toMatch(/background: var\(--seg-on\)/);
        expect(body(`${scope} .voice-part label`)).toMatch(
            /grid-template-columns: minmax\(0, 1.3fr\) minmax\(0, 2fr\) 170px/,
        );
        expect(body(`${scope} .voice-part label`)).toMatch(/border-bottom: 1px solid var\(--line2\)/);
        expect(body(`${scope} .facts dd`)).toMatch(/text-align: right/);
        expect(body(`${scope} .facts`)).toMatch(/margin: 0 -20px/);
        expect(body(`${scope} .facts dt`)).toMatch(/padding-left: 16px;[^}]*color: var\(--faint\)/);
        // The dark theme takes the next stronger token for a fact's label and the dividers, in both of the console's forms.
        for (const root of [dark, darkAuto]) {
            expect(body(`${root} .facts dt`)).toMatch(/border-bottom-color: var\(--line\); color: var\(--mute\)/);
            expect(body(`${root} .voice-part label`)).toMatch(/border-bottom-color: var\(--line\)/);
            expect(body(`${root} .send-mode-choice`)).toMatch(/border-top-color: var\(--line\)/);
        }
        expect(body(`${scope} .send-mode-option`)).toMatch(/padding: 11px 13px;[^}]*border-radius: 12px/);
        expect(body(`${scope} .badge.success`)).toMatch(/background: var\(--green-2\); color: var\(--green\)/);
        expect(body(`${scope} .send-mode-option:has(input:checked)`)).toMatch(/background: var\(--teal-2\)/);
    });
});

describe('Settings drawn in the console', () => {
    beforeEach(() => {
        chats.value = {};
        conversations.value = [];
        attention.loaded.value = false;
        attention.approvals.value = [];
        attention.questions.value = [];
        consoleCampaigns.campaigns.value = [];
        consoleCampaigns.read.value = false;
        mode.value = 'live';
    });
    afterEach(() => cleanup());

    // Every section the screen draws is reached by the card rule, and so is the Runtime card under its wrapper: none is
    // left in the old look beside the others.
    it('has every one of its sections, Live’s included, under the card rule', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending')
            .answer('decision.list', 'decision.list/pending')
            .answer('preference.list', 'preference.list/set')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all')
            .answer('system.info', 'system.info/with-ui');
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
        render(<Console api={fake} desktop={stubDesktop()} stream={stubStream().stream} />);
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        fireEvent.click(
            within(document.querySelector<HTMLElement>('.side')!).getByRole('button', { name: 'Settings' }),
        );
        await screen.findByRole('heading', { name: 'Settings', level: 1 });
        await waitFor(() => expect(document.querySelector('[data-section="runtime"] .card')).not.toBeNull());
        await waitFor(() => expect(document.querySelector('.send-mode-choice')).not.toBeNull());
        const cards = [`${scope} > section`, `${scope} > [data-section='runtime'] > .card`];
        const headings = screen.getAllByRole('heading', { level: 2 });
        expect(headings.map((each) => each.textContent?.trim().split(/\s+/)[0])).toEqual([
            'Appearance',
            'Your',
            'How',
            'Notifications',
            'Models',
            'Runtime',
        ]);
        for (const heading of headings) {
            let at: Element | null = heading;
            while (at !== null && !cards.some((each) => at!.matches(each))) at = at.parentElement;
            expect(at, heading.textContent ?? '').not.toBeNull();
        }
    });
});
