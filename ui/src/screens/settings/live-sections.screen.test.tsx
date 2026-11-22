import { act as flush, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../../api/session';
import { here, chats, conversations } from '../../console/state';
import { Console } from '../../console/console';
import { attention } from '../../console/needs';
import * as consoleCampaigns from '../../console/campaigns';
import { mode, type Mode } from '../../platform/mode';
import { activatePlugins } from '../../platform/plugin';
import { browserPlugins } from '../../plugins';
import { route } from '../../router';
import type { DesktopHost, DesktopSetup } from '../../platform/desktop';
import { setupFound, stubDesktop, stubStream, type StubDesktop } from '../../test/desktop';
import { callsBeyondStart, FakeApi, watch } from '../../test/fake-api';

// Settings in Live gains Models, what Connect holds for the AI hosts and keys, and Runtime, what System holds of the
// runtime, beside the sections it has. Both are the same cards over the same answers; Connect and System keep them.

function renderSettings(where: Mode, status: DesktopSetup = setupFound): { desktop: StubDesktop; fake: FakeApi } {
    mode.value = where;
    const desktop = stubDesktop(status);
    const fake = new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('preference.list', 'preference.list/none')
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
    return { desktop, fake };
}

const settings = async () => {
    await screen.findByRole('heading', { name: 'Who do you want to reach?' });
    const side = within(document.querySelector<HTMLElement>('.side')!);
    fireEvent.click(side.getByRole('button', { name: 'Settings' }));
    await screen.findByRole('heading', { name: 'Settings', level: 1 });
};

beforeEach(() => {
    chats.value = {};
    conversations.value = [];
    attention.loaded.value = false;
    attention.approvals.value = [];
    attention.questions.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
});
afterEach(() => {
    cleanup();
    mode.value = 'live';
});

describe('Settings in Live', () => {
    it('keeps the sections it has and adds Models and Runtime after them', async () => {
        renderSettings('live');
        await settings();
        const headings = () => screen.getAllByRole('heading', { level: 2 }).map((each) => each.textContent?.trim());
        await waitFor(() => expect(headings()).toContain('Models'));
        await waitFor(() => expect(headings().some((each) => each?.startsWith('Runtime'))).toBe(true));
        const order = headings();
        for (const kept of ['Appearance', 'Your name and tone']) expect(order, kept).toContain(kept);
        expect(order.indexOf('Models')).toBeGreaterThan(order.indexOf('Appearance'));
        expect(order.findIndex((each) => each?.startsWith('Runtime'))).toBeGreaterThan(order.indexOf('Models'));
    });

    it('draws in Models what Connect draws of the AI hosts and the keys, from the answer the console already holds', async () => {
        const { desktop } = renderSettings('live');
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'setup.status')).toBe(true));
        const read = desktop.calls.filter((each) => each.name === 'setup.status').length;
        await settings();
        const models = document.querySelector<HTMLElement>('[data-section="models"]')!;
        await waitFor(() => expect(within(models).getByText('Drives the chat')).toBeTruthy());
        expect(within(models).getByText('Not found')).toBeTruthy();
        // No key is saved: no row of keys.
        expect(models.querySelector('.keys-set')).toBeNull();
        expect(desktop.calls.filter((each) => each.name === 'setup.status')).toHaveLength(read);
        // Connect keeps the same cards: nothing it had is taken away.
        flush(() => here().dock.show('connect'));
        const connect = await waitFor(() => document.querySelector<HTMLElement>('[data-pane="connect"]')!);
        expect(within(connect).getByText('Drives the chat')).toBeTruthy();
    });

    it('draws what is on this computer apart from what is not, and keeps every act', async () => {
        const [claude, codex, gemini, deepseek] = setupFound.hosts as [
            DesktopHost,
            DesktopHost,
            DesktopHost,
            DesktopHost,
        ];
        const hosts = [
            { ...claude, runsRoles: 'default' },
            codex,
            gemini,
            { ...gemini, id: 'cursor', label: 'Cursor CLI', found: false, version: null },
            { ...deepseek, id: 'anthropic', label: 'Anthropic', found: true },
            deepseek,
        ];
        const { desktop } = renderSettings('live', { ...setupFound, hosts });
        await settings();
        const models = document.querySelector<HTMLElement>('[data-section="models"]')!;
        await waitFor(() => expect(models.querySelector('.ai-add')).toBeTruthy());
        // On this computer: the programs found, a card each, the one in use marked, then the keys set in one row.
        const here = models.querySelector<HTMLElement>('.ai-here')!;
        const cards = Array.from(here.querySelectorAll<HTMLElement>('.host-row'));
        expect(cards.map((each) => each.querySelector('b')?.textContent)).toEqual(['Claude Code', 'Gemini CLI']);
        expect(cards.map((each) => each.querySelector('.in-use') !== null)).toEqual([true, false]);
        const keys = here.querySelector<HTMLElement>('.keys-set')!;
        expect(keys.querySelector('b')?.textContent).toBe('Model keys');
        expect(Array.from(keys.querySelectorAll('.pillc'), (each) => each.textContent)).toEqual(['Anthropic']);
        // Not here yet: the programs not found, and the keys through the one panel.
        // The card is painted again after each act, so each look reads it afresh.
        const add = () => models.querySelector<HTMLElement>('.ai-add')!;
        expect(add().querySelector('.group-label')?.textContent).toBe('Add or change');
        const absent = add().querySelector<HTMLElement>('.hosts-absent')!;
        expect(absent.textContent).toContain('Codex');
        expect(absent.textContent).toContain('Cursor CLI');
        fireEvent.click(within(absent).getByRole('button', { name: 'Install' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'setup.installProgram')).toBe(true));
        expect(desktop.calls.find((each) => each.name === 'setup.installProgram')!.args).toEqual(['codex']);
        // The panel opens on the first provider with no key: its field and Save key, which saves the typed key.
        const provider = await waitFor(() => within(add()).getByLabelText<HTMLSelectElement>('Provider'));
        expect(Array.from(provider.options, (each) => each.textContent?.trim())).toEqual([
            'Anthropic · key set',
            'DeepSeek',
        ]);
        expect(provider.value).toBe('deepseek');
        await waitFor(() =>
            expect(within(add()).getByRole<HTMLButtonElement>('button', { name: 'Save key' }).disabled).toBe(false),
        );
        fireEvent.input(within(add()).getByLabelText('API key'), { target: { value: ' sk-1 ' } });
        fireEvent.click(within(add()).getByRole('button', { name: 'Save key' }));
        await waitFor(() => expect(desktop.calls.some((each) => each.name === 'setup.setProviderKey')).toBe(true));
        expect(desktop.calls.find((each) => each.name === 'setup.setProviderKey')!.args).toEqual(['deepseek', 'sk-1']);
        // Picking a provider with a key shows its state and Remove the key at once, and no field.
        fireEvent.change(within(add()).getByLabelText('Provider'), { target: { value: 'anthropic' } });
        await waitFor(() => expect(within(add()).getByRole('button', { name: 'Remove the key' })).toBeTruthy());
        expect(within(add()).getByText('Key set', { selector: '.key-state' })).toBeTruthy();
        expect(within(add()).queryByLabelText('API key')).toBeNull();
        fireEvent.click(within(add()).getByRole('button', { name: 'Remove the key' }));
        await waitFor(() =>
            expect(
                desktop.calls.filter((each) => each.name === 'setup.setProviderKey').map((each) => each.args),
            ).toContainEqual(['anthropic', '']),
        );
    });

    it('forgets a key typed and not saved when another provider is chosen', async () => {
        const [claude, , , deepseek] = setupFound.hosts as [DesktopHost, DesktopHost, DesktopHost, DesktopHost];
        const hosts = [claude, deepseek, { ...deepseek, id: 'openai', label: 'OpenAI' }];
        const { desktop } = renderSettings('live', { ...setupFound, hosts });
        await settings();
        const models = document.querySelector<HTMLElement>('[data-section="models"]')!;
        const add = () => models.querySelector<HTMLElement>('.ai-add')!;
        const provider = await waitFor(() => within(add()).getByLabelText<HTMLSelectElement>('Provider'));
        const first = provider.value;
        fireEvent.input(within(add()).getByLabelText('API key'), { target: { value: 'sk-typed' } });
        fireEvent.change(within(add()).getByLabelText('Provider'), {
            target: { value: first === 'openai' ? 'deepseek' : 'openai' },
        });
        await waitFor(() => expect(within(add()).getByLabelText<HTMLSelectElement>('Provider').value).not.toBe(first));
        fireEvent.change(within(add()).getByLabelText('Provider'), { target: { value: first } });
        await waitFor(() => expect(within(add()).getByLabelText<HTMLSelectElement>('Provider').value).toBe(first));
        expect(within(add()).getByLabelText<HTMLInputElement>('API key').value).toBe('');
        expect(desktop.calls.some((each) => each.name === 'setup.setProviderKey')).toBe(false);
    });

    it('draws in Runtime what System draws, from the system.info the page already read', async () => {
        const { fake } = renderSettings('live');
        await settings();
        const runtime = await waitFor(() => document.querySelector<HTMLElement>('[data-section="runtime"]')!);
        for (const label of ['Version', 'Started', 'Address', 'Data directory', 'Database'])
            expect(within(runtime).getByText(label, { selector: 'dt' }), label).toBeTruthy();
        expect(fake.calls.filter((each) => each.operation === 'system.info').length).toBeGreaterThan(0);
        // The console's own read at its start, for the mode strip, is not the section's.
        expect(callsBeyondStart(fake, 'plugin.list')).toEqual([]);
    });

    it('has neither in the demo, which keeps Connect and System in its sidebar', async () => {
        renderSettings('demo');
        await screen.findByRole('heading', { name: 'Who do you want to reach?' });
        const side = within(document.querySelector<HTMLElement>('.side')!);
        fireEvent.click(side.getByRole('button', { name: 'Settings' }));
        await screen.findByRole('heading', { name: 'Settings', level: 1 });
        expect(document.querySelector('[data-section="models"]')).toBeNull();
        expect(document.querySelector('[data-section="runtime"]')).toBeNull();
    });
});

describe('Models, Use on a program found and not in use', () => {
    const [claude, codex, gemini, deepseek] = setupFound.hosts as [DesktopHost, DesktopHost, DesktopHost, DesktopHost];
    const codexFound: DesktopHost = { ...codex, found: true, version: '0.130.0', install: null };
    /** Claude Code in use, Codex found beside it, Gemini CLI found, and a key set. */
    const both = (inUse: 'claude' | 'codex'): DesktopSetup => ({
        ...setupFound,
        hosts: [
            { ...claude, runsRoles: inUse === 'claude' ? 'claude-code' : null },
            { ...codexFound, runsRoles: inUse === 'codex' ? 'codex' : null },
            gemini,
            { ...deepseek, found: true },
        ],
    });
    const models = async () => {
        await settings();
        const section = document.querySelector<HTMLElement>('[data-section="models"]')!;
        await waitFor(() => expect(section.querySelector('.ai-here')).toBeTruthy());
        return section;
    };
    /** Each card on this computer: its name, whether it carries In use, and whether it offers Use. */
    const cards = (section: HTMLElement) =>
        Array.from(section.querySelectorAll<HTMLElement>('.ai-here .host-row'), (card) => [
            card.querySelector('b')?.textContent,
            card.querySelector('.in-use') !== null,
            within(card).queryByRole('button', { name: 'Use' }) !== null,
        ]);

    it('offers Use on a program that could carry In use and does not, never on the one in use, a program that drives no chat or the keys', async () => {
        renderSettings('live', both('claude'));
        const section = await models();
        expect(cards(section)).toEqual([
            ['Claude Code', true, false],
            ['Codex', false, true],
            ['Gemini CLI', false, false],
        ]);
        expect(
            within(section.querySelector<HTMLElement>('.keys-set')!).queryByRole('button', { name: 'Use' }),
        ).toBeNull();
    });

    it('offers none on a Claude Code below its floor', async () => {
        const below = { ...claude, belowFloor: 'Claude Code 2.1.200 is older than 2.1.257.', runsRoles: null };
        renderSettings('live', { ...setupFound, hosts: [below, { ...codexFound, runsRoles: 'codex' }] });
        const section = await models();
        expect(cards(section)).toEqual([
            ['Claude Code', false, false],
            ['Codex', true, false],
        ]);
    });

    it('asks the desktop, then draws what the card reads again: In use moved, and one line of what changed and what did not', async () => {
        const { desktop } = renderSettings('live', both('claude'));
        const section = await models();
        const asked = desktop.setup.useProgram;
        desktop.setup.useProgram = async (id) => {
            await asked(id);
            desktop.status = both('codex');
        };
        fireEvent.click(within(section).getByRole('button', { name: 'Use' }));
        await waitFor(() => expect(section.querySelector('.used-program')).not.toBeNull());
        expect(desktop.calls.filter((each) => each.name === 'setup.useProgram').map((each) => each.args)).toEqual([
            ['codex'],
        ]);
        expect(cards(section)).toEqual([
            ['Claude Code', false, true],
            ['Codex', true, false],
            ['Gemini CLI', false, false],
        ]);
        const line = section.querySelector<HTMLElement>('.used-program')!;
        expect(line.closest('.host-row')?.querySelector('b')?.textContent).toBe('Codex');
        // The runs-roles line already says the roles run on Codex: the line says only what the act did not change.
        expect(line.textContent).toBe(
            "Work already running finishes on the program it started on, and open chats are unchanged: each chat's program is chosen in its own model menu.",
        );
        const codexCard = line.closest<HTMLElement>('.host-row')!;
        expect(codexCard.querySelector('.runs-roles')?.textContent).toBe(
            "Your agents' roles run on Codex, by the execution profile codex.",
        );
        expect(codexCard.textContent?.match(/roles (now )?run on Codex/g)).toHaveLength(1);
        expect(section.querySelector('[role="alert"]')).toBeNull();
    });

    it('says a refusal in the desktop’s words in the alert note, and the card stays as the desktop reads it', async () => {
        const { desktop } = renderSettings('live', both('claude'));
        const refusal =
            'Roles:DefaultExecutionProfile is set by the environment variable JASON_Roles__DefaultExecutionProfile, which overrides the settings file, so nothing was written; remove the variable or change it, then restart the runtime.';
        desktop.refuse.set('setup.useProgram', refusal);
        const section = await models();
        fireEvent.click(within(section).getByRole('button', { name: 'Use' }));
        await waitFor(() => expect(section.querySelector('[role="alert"]')?.textContent).toBe(refusal));
        expect(cards(section)).toEqual([
            ['Claude Code', true, false],
            ['Codex', false, true],
            ['Gemini CLI', false, false],
        ]);
        expect(section.querySelector('.used-program')).toBeNull();
    });
});
