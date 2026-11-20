import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { readFileSync } from 'node:fs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import * as classicCampaigns from '../screens/campaigns/store';
import { chatDesktop, setupFound, stubStream, type StubDesktop } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { choicesOf, chooseIn, currentOf, menuButton, waitForMenus } from '../test/menus';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { chats, conversations } from './state';

// The two menus in a composer's row are drawn by the page, not by the browser's select: what the select gave a person
// is held here. The menu opens from the keyboard and takes the focus on the item chosen; the arrows, Home and End move
// through the items, a disabled one included; a choice or Escape closes it and gives the focus back to the button; Tab
// and a click elsewhere close it; the button says the value chosen; and a choice reaches the same code a select's did.

function renderConsole(desktop: StubDesktop = chatDesktop()): StubDesktop {
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/set')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
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

const calls = (desktop: StubDesktop, name: string) => desktop.calls.filter((each) => each.name === name);
const access = () => menuButton('Access mode');
const model = () => menuButton('Model');
const open = () => screen.queryByRole('menu');
const focused = () => document.activeElement as HTMLElement;
const key = (target: Element, name: string) => fireEvent.keyDown(target, { key: name });
/** Enter or Space on a focused button is a click on it, as a browser does. */
const press = (target: Element) => fireEvent.click(target);

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
    classicCampaigns.shown.value = null;
});

describe('a composer’s menus, as a select was', () => {
    it('are buttons that say they open a menu and the value chosen, and the menu says which item is chosen', async () => {
        renderConsole();
        await waitFor(() => access());
        expect(access().getAttribute('aria-haspopup')).toBe('menu');
        expect(access().getAttribute('aria-expanded')).toBe('false');
        expect(access().getAttribute('aria-label')).toBe('Access mode: Full access');
        expect(model().getAttribute('aria-label')).toBe('Model: Claude Code default');
        expect(document.querySelector('.composer select')).toBeNull();
        fireEvent.click(access());
        const menu = screen.getByRole('menu', { name: 'Access mode: Full access' });
        expect(access().getAttribute('aria-expanded')).toBe('true');
        expect(access().getAttribute('aria-controls')).toBe(menu.id);
        const items = within(menu).getAllByRole('menuitemradio');
        expect(items.map((each) => each.getAttribute('aria-label'))).toEqual([
            'Ask first',
            'Plan only',
            'Auto-edit',
            'Full access',
        ]);
        expect(items.filter((each) => each.getAttribute('aria-checked') === 'true')).toEqual([items[3]]);
        // Each item's line, what it does, is its description.
        const said = document.getElementById(items[1]!.getAttribute('aria-describedby')!);
        expect(said?.textContent).toBe('Reads and proposes, and changes nothing.');
        // Each mode carries its own mark at the left, as the button does, and the marks differ.
        const marks = items.map((each) => each.querySelector('.accmark svg')?.getAttribute('class'));
        expect(marks).toEqual(['glyph-accAsk', 'glyph-accPlan', 'glyph-accEdit', 'glyph-accFull']);
    });

    it('opens from the keyboard on the item chosen, and the arrows, Home and End move through the items, round', async () => {
        renderConsole();
        await waitFor(() => access());
        access().focus();
        key(access(), 'ArrowDown');
        const menu = await waitFor(() => screen.getByRole('menu'));
        expect(focused().getAttribute('aria-label')).toBe('Full access');
        key(menu, 'ArrowDown');
        expect(focused().getAttribute('aria-label')).toBe('Ask first');
        key(menu, 'ArrowUp');
        expect(focused().getAttribute('aria-label')).toBe('Full access');
        key(menu, 'ArrowUp');
        expect(focused().getAttribute('aria-label')).toBe('Auto-edit');
        key(menu, 'ArrowDown');
        expect(focused().getAttribute('aria-label')).toBe('Full access');
        key(menu, 'Home');
        expect(focused().getAttribute('aria-label')).toBe('Ask first');
        key(menu, 'End');
        expect(focused().getAttribute('aria-label')).toBe('Full access');
    });

    it('chooses with Enter: the menu closes, the focus is back on the button, which names the mode, and the desktop is asked', async () => {
        const desktop = renderConsole();
        await waitFor(() => access());
        access().focus();
        key(access(), 'ArrowDown');
        const menu = await waitFor(() => screen.getByRole('menu'));
        // From Full access, the item chosen, round to Ask first and on to Plan only.
        key(menu, 'ArrowDown');
        key(menu, 'ArrowDown');
        press(focused());
        expect(open()).toBeNull();
        expect(focused()).toBe(access());
        await waitFor(() => expect(calls(desktop, 'chat.setMode')).toHaveLength(1));
        expect(calls(desktop, 'chat.setMode')[0]!.args.slice(1)).toEqual(['plan_only', 'claude']);
        await waitFor(() => expect(access().getAttribute('aria-label')).toBe('Access mode: Plan only'));
    });

    it('closes on Escape with nothing chosen and the focus back on the button', async () => {
        const desktop = renderConsole();
        await waitFor(() => access());
        access().focus();
        key(access(), 'ArrowUp');
        const menu = await waitFor(() => screen.getByRole('menu'));
        key(menu, 'End');
        key(menu, 'Escape');
        expect(open()).toBeNull();
        expect(focused()).toBe(access());
        expect(access().getAttribute('aria-expanded')).toBe('false');
        expect(calls(desktop, 'chat.setMode')).toEqual([]);
    });

    it('closes on Tab and on a click elsewhere, choosing nothing', async () => {
        const desktop = renderConsole();
        await waitFor(() => access());
        fireEvent.click(access());
        key(screen.getByRole('menu'), 'Tab');
        expect(open()).toBeNull();
        fireEvent.click(model());
        expect(screen.getByRole('menu', { name: /^Model/ })).toBeTruthy();
        fireEvent.mouseDown(screen.getByRole('textbox', { name: 'Your message' }));
        expect(open()).toBeNull();
        expect(calls(desktop, 'chat.setMode')).toEqual([]);
    });

    it('keeps a mode that cannot be chosen in the arrows’ way, saying why, and choosing it does nothing', async () => {
        const status = structuredClone(setupFound);
        status.hosts.find((each) => each.id === 'codex')!.found = true;
        const desktop = renderConsole(chatDesktop(status));
        await waitFor(() => model());
        chooseIn('Access mode', 'mode', 'ask_first');
        await waitFor(() => expect(access().getAttribute('aria-label')).toBe('Access mode: Ask first'));
        chooseIn('Model', 'host', 'codex');
        await waitFor(() => expect(model().getAttribute('aria-label')).toBe('Model: Codex default'));
        access().focus();
        key(access(), 'ArrowDown');
        const menu = await waitFor(() => screen.getByRole('menu'));
        key(menu, 'End');
        const full = focused();
        expect(full.getAttribute('aria-label')).toBe('Full access');
        expect(full.getAttribute('aria-disabled')).toBe('true');
        expect(document.getElementById(full.getAttribute('aria-describedby')!)?.textContent).toBe(
            'Full access runs on Claude Code only.',
        );
        press(full);
        expect(open()).not.toBeNull();
        expect(calls(desktop, 'chat.setMode').map((each) => each.args[1])).toEqual(['ask_first']);
        expect(access().getAttribute('aria-label')).toBe('Access mode: Ask first');
    });

    it('draws the model menu with our content and chooses a model and an effort from the keyboard, sent with the turn', async () => {
        const desktop = renderConsole();
        await waitFor(() => model());
        expect(choicesOf('Model', 'model').map((each) => [each.text, each.checked])).toEqual([
            ['Default (recommended)', true],
            ['Opus', false],
            ['Sonnet', false],
            ['Haiku', false],
            ['Another model…', false],
        ]);
        model().focus();
        key(model(), 'ArrowDown');
        const menu = await waitFor(() => screen.getByRole('menu', { name: /^Model/ }));
        // The focus opens on the model chosen; the host tabs come before it in the arrows' order.
        expect(focused().getAttribute('aria-label')).toBe('Default (recommended)');
        key(menu, 'ArrowDown');
        expect(focused().getAttribute('aria-label')).toBe('Opus');
        press(focused());
        expect(open()).toBeNull();
        expect(focused()).toBe(model());
        expect(model().getAttribute('aria-label')).toBe('Model: Opus');
        // The effort steps: a step stays in the menu, so a person may move along them.
        fireEvent.click(model());
        const steps = within(screen.getByRole('group', { name: 'Effort' })).getAllByRole('menuitemradio');
        expect(steps.map((each) => each.getAttribute('aria-label') ?? each.textContent)).toEqual([
            'Low',
            'Medium',
            'High',
            'Extra high',
            'Max',
            'Default effort',
        ]);
        press(steps[2]!);
        expect(model().getAttribute('aria-label')).toBe('Model: Opus, High effort');
        key(screen.getByRole('menu', { name: /^Model/ }), 'Escape');
        expect(focused()).toBe(model());
        fireEvent.change(screen.getByRole('textbox', { name: 'Your message' }), { target: { value: 'Go' } });
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));
        await waitFor(() => expect(calls(desktop, 'chat.send')).toHaveLength(1));
        expect(calls(desktop, 'chat.send')[0]!.args.slice(1)).toEqual([
            'Go',
            { host: 'claude', model: 'opus', effort: 'high' },
        ]);
    });

    it('takes a typed model name in the menu, where Enter closes the menu and sends nothing', async () => {
        const desktop = renderConsole();
        await waitFor(() => model());
        fireEvent.click(model());
        fireEvent.click(screen.getByRole('menuitemradio', { name: 'Another model…' }));
        const name = await waitFor(() => screen.getByRole('textbox', { name: 'Model name' }));
        expect(focused()).toBe(name);
        fireEvent.change(name, { target: { value: 'claude-opus-5-5' } });
        key(name, 'Enter');
        expect(open()).toBeNull();
        expect(focused()).toBe(model());
        expect(model().getAttribute('aria-label')).toBe('Model: claude-opus-5-5');
        expect(calls(desktop, 'chat.send')).toEqual([]);
    });
});

describe('a composer’s menus, beyond what a select gave', () => {
    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('closes when the focus leaves it any other way: moved by the page, or the window left', async () => {
        renderConsole();
        await waitFor(() => access());
        fireEvent.click(access());
        expect(open()).not.toBeNull();
        // Moved by the page, with no key and no click: the menu goes.
        act(() => screen.getByRole('textbox', { name: 'Your message' }).focus());
        expect(open()).toBeNull();
        // The window left: the focus goes nowhere in the page, and the menu goes as a select's list does.
        fireEvent.click(model());
        const item = within(screen.getByRole('menu', { name: /^Model/ })).getAllByRole('menuitemradio')[0]!;
        fireEvent.blur(item, { relatedTarget: null });
        expect(open()).toBeNull();
    });

    it('keeps the focus where it is on a press inside the menu, so a browser that moves no focus on a click still chooses', async () => {
        const desktop = renderConsole();
        await waitFor(() => access());
        fireEvent.click(access());
        const plan = screen.getByRole('menuitemradio', { name: 'Plan only' });
        // A mousedown the menu lets through would take the focus off the item, and the menu with it, before the click.
        expect(fireEvent.mouseDown(plan)).toBe(false);
        expect(fireEvent.mouseDown(screen.getByRole('menu'))).toBe(false);
        fireEvent.click(plan);
        await waitFor(() => expect(calls(desktop, 'chat.setMode')).toHaveLength(1));
        // The typed model name takes its own press, which places the caret.
        fireEvent.click(model());
        fireEvent.click(screen.getByRole('menuitemradio', { name: 'Another model…' }));
        const name = await waitFor(() => screen.getByRole('textbox', { name: 'Model name' }));
        expect(fireEvent.mouseDown(name)).toBe(true);
    });

    it('opens downward where there is no room for it above the button, and upward where there is', async () => {
        renderConsole();
        await waitFor(() => access());
        let buttonTop = 120;
        vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
            const box = (top: number, height: number) =>
                ({ top, bottom: top + height, height, left: 0, right: 0, width: 0, x: 0, y: top }) as DOMRect;
            if (this.classList.contains('cmenu-panel')) return box(0, 320);
            if (this.classList.contains('cmenu')) return box(buttonTop, 32);
            return box(0, 600);
        });
        // At the desktop's smallest window the start screen's box sits 120 px down: a 320 px menu has no room above.
        fireEvent.click(model());
        expect(screen.getByRole('menu', { name: /^Model/ }).classList.contains('down')).toBe(true);
        key(screen.getByRole('menu', { name: /^Model/ }), 'Escape');
        buttonTop = 500;
        fireEvent.click(access());
        expect(screen.getByRole('menu').classList.contains('down')).toBe(false);
    });

    it('draws the access mode’s own mark on the button: a shield with ! or ?, a pencil, an eye', async () => {
        renderConsole();
        await waitFor(() => access());
        const mark = () => access().querySelector('svg')?.getAttribute('class');
        expect(mark()).toBe('glyph-accFull');
        chooseIn('Access mode', 'mode', 'ask_first');
        await waitFor(() => expect(mark()).toBe('glyph-accAsk'));
        chooseIn('Access mode', 'mode', 'plan_only');
        await waitFor(() => expect(mark()).toBe('glyph-accPlan'));
        chooseIn('Access mode', 'mode', 'auto_edit');
        await waitFor(() => expect(mark()).toBe('glyph-accEdit'));
        chooseIn('Access mode', 'mode', 'full_access');
        await waitFor(() => expect(mark()).toBe('glyph-accFull'));
        const paths = (name: string) =>
            [...access().querySelectorAll(`svg.glyph-${name} path`)].map((each) => each.getAttribute('d'));
        expect(paths('accFull')).toHaveLength(3);
    });

    it('keeps the chat’s box at one line, growing with its words to the sheet’s limit and scrolling past it', async () => {
        renderConsole();
        const box = await waitFor(() => screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'Your message' }));
        expect(box.getAttribute('rows')).toBe('1');
        // The box is as tall as its words: the page measures them and sets the height the sheet caps.
        let words = 26;
        Object.defineProperty(box, 'scrollHeight', { configurable: true, get: () => words });
        words = 130;
        fireEvent.change(box, { target: { value: ['One', 'Two', 'Three', 'Four', 'Five'].join('\n') } });
        expect(box.style.getPropertyValue('--fit')).toBe('130px');
        words = 26;
        fireEvent.change(box, { target: { value: '' } });
        expect(box.style.getPropertyValue('--fit')).toBe('26px');
        const sheet = readFileSync('src/console/composer.css', 'utf8');
        expect(sheet).toMatch(/[.]composer textarea [{][^}]*height: var[(]--fit, auto[)]/);
    });

    it('reads a closed menu inside a wait that fails as a failure, not as a wait that never ends', async () => {
        renderConsole();
        await waitFor(() => access());
        // Each read of a closed menu opens it and closes it again; a wait that re-ran on every change to the page would
        // read again on its own change, never reaching its time limit.
        await expect(
            waitForMenus(() => expect(currentOf('Access mode', 'mode')).toBe('never_a_mode'), { timeout: 200 }),
        ).rejects.toThrow();
    });
});

describe('the line under a composer', () => {
    it('says the access mode and what it does, outside the box', async () => {
        renderConsole();
        await waitFor(() => access());
        const form = screen.getByRole('form', { name: 'Message' });
        const line = () => form.querySelector(':scope > .under .accline');
        expect(line()?.textContent).toBe(
            "Full access · the AI runs commands without asking, except those that reach Jason's own settings, data or variables",
        );
        expect(form.querySelector('.composer .under')).toBeNull();
        chooseIn('Access mode', 'mode', 'ask_first');
        await waitFor(() =>
            expect(line()?.textContent).toBe('Ask first · asks on a card before anything Jason does not settle itself'),
        );
        chooseIn('Access mode', 'mode', 'plan_only');
        await waitFor(() =>
            expect(form.querySelector(':scope > .under .accline')?.textContent).toBe(
                'Plan only · reads and proposes, and changes nothing',
            ),
        );
    });
});

describe('the composer’s sheet in the dark theme', () => {
    // Read from the file: the test run hands a stylesheet import back empty. The run starts in ui/.
    const sheet = readFileSync('src/console/composer.css', 'utf8').replace(/[/][*][^]*?[*][/]/g, '');
    const selectors = Array.from(sheet.matchAll(/([^{}@]+)[{][^{}]*[}]/g), (rule) => rule[1]!.trim());
    const light = selectors.filter((each) => each.startsWith('.jc-console'));
    const dark = (prefix: string) =>
        selectors.filter((each) => each.startsWith(prefix)).map((each) => each.slice(prefix.length));
    const chosen = dark(":root[data-theme='dark'] ");
    const system = dark(":root:not([data-theme='light']) ");

    it('draws every literal colour from a dark token, chosen and by the system alike', () => {
        const literal = Array.from(
            sheet.matchAll(/([^{}@]+)[{]([^{}]*)[}]/g),
            (rule) => [rule[1]!.trim(), rule[2]!] as const,
        )
            .filter(([selector, body]) => selector.startsWith('.jc-console') && /#[0-9a-f]{3,8}\b/i.test(body))
            .map(([selector]) => selector);
        expect(literal.length).toBeGreaterThan(0);
        for (const each of literal) {
            expect(chosen, each).toContain(each);
            expect(system, each).toContain(each);
        }
        expect(system.sort()).toEqual(chosen.sort());
    });

    it('overrides a state of a rule it darkens too, where the state sets what the dark rule sets', () => {
        const rules = Array.from(sheet.matchAll(/([^{}@]+)[{]([^{}]*)[}]/g), (rule) => ({
            selector: rule[1]!.trim(),
            props: Array.from(rule[2]!.matchAll(/([a-z-]+)\s*:/g), (each) => each[1]!),
        }));
        const propsOf = (selector: string) => rules.find((each) => each.selector === selector)?.props ?? [];
        const states = (base: string) =>
            light.filter((each) => each.startsWith(base) && /^[.:[]/.test(each.slice(base.length)));
        let held = 0;
        for (const base of chosen) {
            const darkened = propsOf(`:root[data-theme='dark'] ${base}`);
            for (const state of states(base))
                if (propsOf(state).some((each) => darkened.includes(each))) {
                    held++;
                    expect(chosen, state).toContain(state);
                }
        }
        expect(held).toBeGreaterThan(0);
    });
});
