import { readFileSync } from 'node:fs';
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { mode } from '../../platform/mode';
import { voiceKnown } from './voice-state';
import { startLocale } from '../../platform/i18n';
import { setTheme } from '../../platform/theme';
import { route } from '../../router';
import { FakeApi } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';

afterEach(() => {
    cleanup();
    setTheme('system');
    mode.value = 'live';
    voiceKnown.value = 'unread';
});

describe('Settings', () => {
    it('draws no language choice, and reads none that this browser stored before', async () => {
        localStorage.setItem('jason.ui.locale', 'uk');
        try {
            const fake = new FakeApi().answer('preference.list', 'preference.list/none');
            renderApp(fake);
            route.value = { screen: 'settings' };
            await waitFor(() => screen.getByRole('heading', { name: 'Settings' }));
            expect(screen.queryByRole('heading', { name: 'Language', level: 2 })).toBeNull();
            expect(screen.queryByRole('button', { name: 'English' })).toBeNull();
            expect(screen.getByRole('link', { name: 'Agents' })).toBeTruthy();
            startLocale();
            expect(document.documentElement.lang).toBe('en');
            expect(fake.calls.map((c) => c.operation)).toEqual(['preference.list', 'system.info']);
        } finally {
            localStorage.removeItem('jason.ui.locale');
        }
    });

    it('marks the Appearance section with an icon, hidden from a screen reader', async () => {
        renderApp(new FakeApi().answer('preference.list', 'preference.list/none'));
        route.value = { screen: 'settings' };
        await waitFor(() => screen.getByRole('heading', { name: 'Appearance' }));
        const heading = screen.getByRole('heading', { name: 'Appearance', level: 2 });
        expect(heading.querySelector('svg.settings-icon[aria-hidden="true"]')).toBeTruthy();
    });

    it("sets each icon on the line of its heading's words, in the browser's own stylesheet", () => {
        // Read from the file: the test run hands a stylesheet import back empty. The run starts in ui/.
        const sheet = readFileSync('src/styles/app.css', 'utf8').replace(/[/][*][^]*?[*][/]/g, '');
        const rule = Array.from(sheet.matchAll(/([^{}]+)[{]([^{}]*)[}]/g)).find(
            (each) => each[1]!.trim() === '.settings-icon',
        );
        expect(rule?.[2]).toMatch(/vertical-align:\s*-2px;/);
        expect(rule?.[2]).toMatch(/color:\s*var\(--accent-text\);/);
    });

    it('sets the theme on the document', async () => {
        renderApp(new FakeApi().answer('preference.list', 'preference.list/none'));
        route.value = { screen: 'settings' };
        await waitFor(() => screen.getByRole('heading', { name: 'Settings' }));
        fireEvent.click(screen.getByRole('button', { name: 'Dark' }));
        expect(document.documentElement.dataset['theme']).toBe('dark');
        expect(screen.getByRole('button', { name: 'Dark' }).getAttribute('aria-pressed')).toBe('true');
    });
});

describe('Appearance', () => {
    it('offers Light, Dark and the system in that order, and marks the one in force', async () => {
        setTheme('dark');
        renderApp(new FakeApi().answer('preference.list', 'preference.list/none'));
        route.value = { screen: 'settings' };
        await waitFor(() => screen.getByRole('heading', { name: 'Appearance', level: 2 }));
        const buttons = ['Light', 'Dark', 'Same as the system'].map((name) => screen.getByRole('button', { name }));
        const pressed = () => buttons.map((each) => each.getAttribute('aria-pressed'));
        expect(pressed()).toEqual(['false', 'true', 'false']);
        const before = (a: HTMLElement, b: HTMLElement) =>
            a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING;
        expect(before(buttons[0]!, buttons[1]!)).toBeTruthy();
        expect(before(buttons[1]!, buttons[2]!)).toBeTruthy();
        fireEvent.click(buttons[0]!);
        expect(pressed()).toEqual(['true', 'false', 'false']);
    });
});

const voice = async (fake: FakeApi) => {
    renderApp(fake);
    route.value = { screen: 'settings' };
    await waitFor(() => screen.getByRole('heading', { name: 'Your name and tone', level: 2 }));
};

describe('Your name and tone', () => {
    it('is two parts, each a heading with no question mark, and neutral is preselected and last', async () => {
        const fake = new FakeApi().answer('preference.list', 'preference.list/none');
        await voice(fake);
        await waitFor(() => screen.getByRole('heading', { name: 'Who writes', level: 3 }));
        screen.getByRole('heading', { name: 'How you write', level: 3 });
        const grammar = screen.getByRole('combobox', { name: 'Grammar forms for the writer' }) as HTMLSelectElement;
        expect(grammar.value).toBe('neutral');
        expect(Array.from(grammar.options).map((each) => each.value)).toEqual(['feminine', 'masculine', 'neutral']);
        expect(screen.getByText(/languages whose grammar marks the writer/)).toBeTruthy();
        expect((screen.getByRole('textbox', { name: 'Name' }) as HTMLInputElement).value).toBe('');
    });

    it('shows what is set, and saves the name with no reason, as the paired person', async () => {
        const fake = new FakeApi('Marta')
            .answer('preference.list', 'preference.list/set')
            .answerAnyRequest('preference.set', 'preference.set/sender');
        await voice(fake);
        await waitFor(() =>
            expect((screen.getByRole('textbox', { name: 'Name' }) as HTMLInputElement).value).toBe('Marta Lind'),
        );
        fireEvent.change(screen.getByRole('textbox', { name: 'Company' }), {
            target: { value: 'Example Analytics Ltd' },
        });
        const form = screen.getByRole('form', { name: 'Who writes' });
        fireEvent.click(within(form).getByRole('button', { name: 'Save' }));
        await waitFor(() => within(form).getByText('Saved.'));
        const sent = fake.sent('preference.set') as Record<string, unknown>;
        expect(sent).toEqual({
            key: 'sender.identity',
            value: {
                senders: [
                    {
                        name: 'Marta Lind',
                        addressed_as: 'Marta',
                        gender_forms: 'feminine',
                        title: 'Head of Partnerships',
                        company: 'Example Analytics Ltd',
                    },
                ],
            },
        });
        expect('reason' in sent).toBe(false);
        // The save read the preferences again before it wrote.
        expect(fake.calls.map((c) => c.operation).filter((o) => o.startsWith('preference'))).toEqual([
            'preference.list',
            'preference.list',
            'preference.set',
        ]);
    });

    it('saves nothing for an empty summary or an empty name, and says why', async () => {
        const fake = new FakeApi().answer('preference.list', 'preference.list/none');
        await voice(fake);
        const how = await waitFor(() => screen.getByRole('form', { name: 'How you write' }));
        fireEvent.click(within(how).getByRole('button', { name: 'Save' }));
        await waitFor(() =>
            within(how).getByText('Write a few words to save this part. Nothing is saved without them.'),
        );
        const who = screen.getByRole('form', { name: 'Who writes' });
        fireEvent.click(within(who).getByRole('button', { name: 'Save' }));
        await waitFor(() => within(who).getByText('Add a name to save this part.'));
        expect(fake.calls.some((c) => c.operation === 'preference.set')).toBe(false);
    });

    it('in the demo says where it is set, draws no form, and asks the runtime nothing', async () => {
        mode.value = 'demo';
        const fake = new FakeApi();
        await voice(fake);
        screen.getByText('Your name and tone are set in Live.');
        expect(screen.queryByRole('form', { name: 'Who writes' })).toBeNull();
        expect(fake.calls.map((c) => c.operation)).toEqual(['system.info']);
    });
});

describe('Your name and tone, and the note on the start screen', () => {
    it('ends the note on the start screen in the same session', async () => {
        voiceKnown.value = 'none';
        const fake = new FakeApi('Marta')
            .answer('preference.list', 'preference.list/none')
            .answerAnyRequest('preference.set', 'preference.set/sender');
        await voice(fake);
        const who = await waitFor(() => screen.getByRole('form', { name: 'Who writes' }));
        fireEvent.change(within(who).getByRole('textbox', { name: 'Name' }), { target: { value: 'Ada' } });
        fireEvent.click(within(who).getByRole('button', { name: 'Save' }));
        await waitFor(() => within(who).getByText('Saved.'));
        expect(voiceKnown.value).toBe('some');
    });
});
