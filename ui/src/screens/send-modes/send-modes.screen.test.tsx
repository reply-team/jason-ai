import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiProvider } from '../../api/context';
import type { Approval, Campaign } from '../../api/types';
import { ApprovalPanel } from '../attention/approval-panel';
import { campaign as open } from '../campaigns/store';
import { FakeApi } from '../../test/fake-api';
import { fixtures } from '../../test/fixtures';
import { AgentSendModes } from './agent-modes';
import { reminderKey } from './reminder';
import { SendModesSettings } from './settings-section';

// The recordings that carry a person's act were made by Marta. A card reads the preferences and its agent for the
// reminder about name and tone; unless a test says otherwise, every mode waits, so there is none.
const marta = () =>
    new FakeApi('Marta')
        .answer('preference.list', 'preference.list/none')
        .answer('campaign.get', 'campaign.get/details');
const operations = (api: FakeApi) => api.calls.map((call) => call.operation);
const voiceLine =
    'Jason now writes in your name without your look. Saying who writes and how, in Settings under "Your name and tone", makes those messages err less.';

const card = (name: 'approval.get/pending-with-preview' | 'approval.get/modes-chosen' | 'approval.get/with-doubts') =>
    structuredClone(fixtures[name].response) as unknown as Approval;

function mount(api: FakeApi, approval: Approval, onApprove = vi.fn()) {
    render(
        <ApiProvider api={api}>
            <ApprovalPanel
                approval={approval}
                now={new Date('2026-10-15T12:05:00.000Z')}
                zone="UTC"
                busy={false}
                remaining="1 more"
                onApprove={onApprove}
                onReject={() => {}}
                onSkip={() => {}}
                onClose={() => {}}
            />
        </ApiProvider>,
    );
    return onApprove;
}

const remember = () => screen.getAllByRole('button', { name: 'Remember this' });
const groupOf = (legend: string) => screen.getByRole('group', { name: legend });
const radio = (legend: string, choice: string) => {
    const radios = groupOf(legend).querySelectorAll('input[type=radio]');
    const labels = Array.from(radios).map((radio) => radio.closest('label')?.textContent ?? '');
    return radios[labels.findIndex((label) => label.trim().startsWith(choice))] as HTMLInputElement;
};

beforeEach(() => localStorage.removeItem(reminderKey));
afterEach(() => {
    cleanup();
    localStorage.removeItem(reminderKey);
    vi.useRealTimers();
});

describe('the two-modes question on an approval card', () => {
    it('asks cold and warm until each is answered, and keeps an answer only when the person presses Remember this', async () => {
        const api = marta().answer('preference.set', 'preference.set/send-mode-cold', 'preference.set/send-mode-warm');
        const onApprove = mount(api, card('approval.get/pending-with-preview'));

        expect(screen.getByRole('heading', { name: 'How should messages go?' })).toBeTruthy();
        expect(groupOf('People who have not replied yet')).toBeTruthy();
        expect(groupOf('People who have replied')).toBeTruthy();
        // Nothing is chosen for the person and nothing is sent by looking at it.
        expect(remember().every((button) => (button as HTMLButtonElement).disabled)).toBe(true);
        // What the card reads is the reminder's, which says nothing here: every mode in force waits.
        await waitFor(() => expect(operations(api)).toEqual(['preference.list', 'campaign.get']));

        fireEvent.click(radio('People who have not replied yet', 'Goes automatically'));
        expect(api.sent('preference.set')).toBeUndefined();
        // Nothing is on yet, so nothing says it is.
        expect(screen.queryByText(voiceLine)).toBeNull();
        fireEvent.click(remember()[0]!);

        await waitFor(() =>
            expect(screen.getByText('Remembered. People who have not replied yet: Goes automatically.')).toBeTruthy(),
        );
        expect(api.sent('preference.set')).toEqual({
            key: 'send_mode.cold',
            value: { mode: 'automatic' },
            reason: "the person's answer",
        });
        // The mode is on at once; the recommendation beside it asks nothing of the person.
        const line = screen.getByText(voiceLine).closest('p')!;
        expect(line.querySelectorAll('input, select, textarea')).toHaveLength(0);
        expect(Array.from(line.querySelectorAll('button')).map((button) => button.textContent)).toEqual([
            'Open Settings',
        ]);

        fireEvent.click(radio('People who have replied', 'Every message waits for you'));
        fireEvent.click(remember()[0]!);
        await waitFor(() =>
            expect(screen.getByText('Remembered. People who have replied: Every message waits for you.')).toBeTruthy(),
        );
        expect(api.sent('preference.set')).toEqual({
            key: 'send_mode.warm',
            value: { mode: 'every_message' },
            reason: "the person's answer",
        });
        expect(screen.getByText(voiceLine)).toBeTruthy();

        // Approving never waited on the question.
        fireEvent.click(screen.getByRole('button', { name: 'Approve' }));
        expect(onApprove).toHaveBeenCalledOnce();
    });

    it('says nothing of name and tone when the choice remembered is that every message waits', async () => {
        const api = marta().answer('preference.set', 'preference.set/send-mode-warm');
        mount(api, card('approval.get/pending-with-preview'));
        fireEvent.click(radio('People who have replied', 'Every message waits for you'));
        fireEvent.click(remember()[1]!);
        await screen.findByText('Remembered. People who have replied: Every message waits for you.');
        expect(screen.queryByText(voiceLine)).toBeNull();
    });

    it('asks only the kind that is still unset', async () => {
        const approval = card('approval.get/pending-with-preview');
        approval.send_modes_unset = fixtures['approval.list/one-mode-unset'].response.items[0]!.send_modes_unset;
        mount(marta(), approval);

        expect(screen.getByRole('group', { name: 'People who have replied' })).toBeTruthy();
        expect(screen.queryByRole('group', { name: 'People who have not replied yet' })).toBeNull();
        await act(async () => {});
    });

    it('is not on a card whose modes are answered, and is not on a card already decided', async () => {
        mount(marta().answer('preference.list', 'preference.list/set'), card('approval.get/modes-chosen'));
        expect(screen.queryByRole('heading', { name: 'How should messages go?' })).toBeNull();
        await act(async () => {});
        cleanup();
        const decided = card('approval.get/pending-with-preview');
        decided.status = 'approved';
        mount(marta(), decided);
        expect(screen.queryByRole('heading', { name: 'How should messages go?' })).toBeNull();
    });
});

describe('the quiet reminder about name and tone', () => {
    const reminder = /Your name and tone are not set\./;

    it('is not shown while both kinds were answered that every message waits', async () => {
        const api = marta().answer('preference.list', 'preference.list/modes-every-message');
        mount(api, card('approval.get/modes-chosen'));
        await waitFor(() => expect(operations(api)).toEqual(['preference.list', 'campaign.get']));
        await act(async () => {});
        expect(screen.queryByText(reminder)).toBeNull();
    });

    it('shows while one kind goes automatically and no name is set, and hides for seven days', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-11-14T10:00:00Z'));
        const api = marta().answer('preference.list', 'preference.list/mode-automatic-voice-unset');
        mount(api, card('approval.get/modes-chosen'));

        const hide = await screen.findByRole('button', { name: 'Hide for 7 days' });
        expect(screen.getByText(reminder)).toBeTruthy();
        expect(screen.getByRole('button', { name: 'Open Settings' })).toBeTruthy();
        // The installation's own mode said enough: the agent is not read.
        expect(operations(api)).toEqual(['preference.list']);
        fireEvent.click(hide);
        expect(screen.queryByText(reminder)).toBeNull();
        expect(localStorage.getItem(reminderKey)).toBe(String(new Date('2026-11-21T10:00:00Z').getTime()));

        // Another card, six days on: still hidden, and the preferences are not even read.
        cleanup();
        vi.setSystemTime(new Date('2026-11-20T10:00:00Z'));
        const quiet = marta();
        mount(quiet, card('approval.get/modes-chosen'));
        expect(screen.queryByText(reminder)).toBeNull();
        expect(quiet.calls).toEqual([]);

        // Eight days on it is shown again.
        cleanup();
        vi.setSystemTime(new Date('2026-11-22T10:00:00Z'));
        mount(
            marta().answer('preference.list', 'preference.list/mode-automatic-voice-unset'),
            card('approval.get/modes-chosen'),
        );
        await screen.findByText(reminder);
    });

    it('shows while the agent in front goes automatically, though nothing is chosen for every agent', async () => {
        const api = marta().answer('campaign.get', 'campaign.get/send-mode-override');
        mount(api, card('approval.get/pending-with-preview'));
        await screen.findByText(reminder);
        expect(api.sent('campaign.get')).toEqual({ campaign_id: 'cmp_1' });
    });

    it('is not shown when both are set, though a mode goes automatically', async () => {
        const api = marta().answer('preference.list', 'preference.list/set');
        mount(api, card('approval.get/modes-chosen'));
        await waitFor(() => expect(operations(api)).toEqual(['preference.list']));
        await act(async () => {});
        expect(screen.queryByText(reminder)).toBeNull();
    });

    it('is shown, and hides, when this browser keeps nothing', async () => {
        const stored = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
            throw new Error('blocked');
        });
        const keeping = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
            throw new Error('blocked');
        });
        try {
            mount(
                marta().answer('preference.list', 'preference.list/mode-automatic-voice-unset'),
                card('approval.get/modes-chosen'),
            );
            fireEvent.click(await screen.findByRole('button', { name: 'Hide for 7 days' }));
            expect(screen.queryByText(reminder)).toBeNull();
        } finally {
            stored.mockRestore();
            keeping.mockRestore();
        }
    });
});

describe('the doubts on an approval card', () => {
    it('says who doubted and why, with the sentence they wrote as text', () => {
        mount(marta(), card('approval.get/with-doubts'));

        expect(screen.getByRole('heading', { name: 'Doubts about this message' })).toBeTruthy();
        expect(
            screen.getByText(
                'A person asked you to look at this: It touches a price, a contract, a legal matter or a complaint.',
            ),
        ).toBeTruthy();
        expect(screen.getByText('Their words: She asks about a price.')).toBeTruthy();
    });

    it('shows a doubt the card names by a code this build does not know, as it is, and none where none stands', () => {
        const unknown = card('approval.get/with-doubts');
        (unknown.preview['doubts'] as { code: string; source: string }[])[0]!.code = 'a_new_code';
        (unknown.preview['doubts'] as { source: string }[])[0]!.source = 'review';
        mount(marta(), unknown);
        expect(screen.getByText('A person doubted this: a_new_code')).toBeTruthy();
        cleanup();
        mount(marta(), card('approval.get/pending-with-preview'));
        expect(screen.queryByRole('heading', { name: 'Doubts about this message' })).toBeNull();
    });
});

describe('the agent’s own send modes', () => {
    const recorded = () => structuredClone(fixtures['campaign.get/send-mode-override'].response) as unknown as Campaign;

    function Shown() {
        return open.value ? <AgentSendModes campaign={open.value} /> : null;
    }
    function mountAgent(api: FakeApi, campaign: Campaign = recorded()) {
        open.value = campaign;
        render(
            <ApiProvider api={api}>
                <Shown />
            </ApiProvider>,
        );
    }
    afterEach(() => {
        open.value = null;
    });

    it('says what each kind goes under and where that comes from, and gives back and sets the agent’s own', async () => {
        const api = marta().answer(
            'campaign.set_send_mode',
            'campaign.set_send_mode/cleared',
            'campaign.set_send_mode/warm-automatic',
        );
        mountAgent(api);

        expect(screen.getByText(/Goes automatically/, { selector: 'p' }).textContent).toContain('set for this agent');
        expect(screen.getByText(/Every message waits for you/, { selector: 'p' }).textContent).toContain(
            'the default, since nothing is chosen',
        );
        expect(
            screen.getAllByRole('button', { name: 'Apply' }).every((button) => (button as HTMLButtonElement).disabled),
        ).toBe(true);

        // Giving the cold mode back to the choice for every agent.
        fireEvent.change(screen.getByRole('combobox', { name: 'People who have not replied yet' }), {
            target: { value: '' },
        });
        fireEvent.click(screen.getAllByRole('button', { name: 'Apply' })[0]!);
        await waitFor(() => expect(screen.getAllByText('Applied.')).toHaveLength(1));
        expect(api.sent('campaign.set_send_mode')).toEqual({ campaign_id: 'cmp_1', cold: null });
        expect(screen.queryByText(/set for this agent/)).toBeNull();

        // Setting the warm mode of this agent alone.
        fireEvent.change(screen.getByRole('combobox', { name: 'People who have replied' }), {
            target: { value: 'automatic' },
        });
        fireEvent.click(screen.getAllByRole('button', { name: 'Apply' })[1]!);
        await waitFor(() =>
            expect(screen.getByText(/Goes automatically/, { selector: 'p' }).textContent).toContain(
                'set for this agent',
            ),
        );
        expect(api.sent('campaign.set_send_mode')).toEqual({ campaign_id: 'cmp_1', warm: 'automatic' });
        await act(async () => {});
    });

    it('draws nothing for an agent read from a runtime older than the send modes', () => {
        const old = recorded();
        delete old.send_modes;
        delete old.send_modes_in_force;
        mountAgent(marta(), old);
        expect(screen.queryByRole('heading', { name: 'How messages go' })).toBeNull();
    });
});

describe('the send modes in Settings', () => {
    const inGroup = (legend: string) => within(groupOf(legend));
    const checkedIn = (legend: string) =>
        Array.from(groupOf(legend).querySelectorAll('input')).some((each) => each.checked);
    const unsetLine = 'Not answered yet, so every message waits for you, and an approval card asks.';
    const cold = 'People who have not replied yet';
    const warm = 'People who have replied';
    const mountSettings = (api: FakeApi) =>
        render(
            <ApiProvider api={api}>
                <SendModesSettings />
            </ApiProvider>,
        );

    it('turns a mode that goes automatically off, answers the other kind, and gives an answer back', async () => {
        const api = marta()
            .answer('preference.list', 'preference.list/set')
            .answer('preference.set', 'preference.set/send-mode-cold-every-message', 'preference.set/send-mode-warm')
            .answer('preference.unset', 'preference.unset/send-mode-cold');
        mountSettings(api);

        expect(await screen.findByRole('heading', { name: 'How messages go', level: 2 })).toBeTruthy();
        expect(radio(cold, 'Goes automatically').checked).toBe(true);
        expect(inGroup(warm).getByText(unsetLine)).toBeTruthy();
        expect(checkedIn(warm)).toBe(false);
        expect(inGroup(warm).queryByRole('button', { name: 'Forget my answer' })).toBeNull();
        expect((inGroup(cold).getByRole('button', { name: 'Save' }) as HTMLButtonElement).disabled).toBe(true);
        expect(operations(api)).toEqual(['preference.list']);

        // Turning "Goes automatically" off from the page, without a terminal.
        fireEvent.click(radio(cold, 'Every message waits for you'));
        expect(api.sent('preference.set')).toBeUndefined();
        fireEvent.click(inGroup(cold).getByRole('button', { name: 'Save' }));
        await waitFor(() => expect(inGroup(cold).getByText('Saved.')).toBeTruthy());
        expect(api.sent('preference.set')).toEqual({
            key: 'send_mode.cold',
            value: { mode: 'every_message' },
            reason: "the person's answer",
        });
        expect(radio(cold, 'Every message waits for you').checked).toBe(true);

        fireEvent.click(radio(warm, 'Every message waits for you'));
        fireEvent.click(inGroup(warm).getByRole('button', { name: 'Save' }));
        await waitFor(() => expect(inGroup(warm).getByText('Saved.')).toBeTruthy());
        expect(api.sent('preference.set')).toEqual({
            key: 'send_mode.warm',
            value: { mode: 'every_message' },
            reason: "the person's answer",
        });
        expect(inGroup(warm).queryByText(unsetLine)).toBeNull();

        // Giving the cold answer back leaves it unanswered, which waits for the person and is asked again.
        fireEvent.click(inGroup(cold).getByRole('button', { name: 'Forget my answer' }));
        await waitFor(() => expect(inGroup(cold).getByText(unsetLine)).toBeTruthy());
        expect(api.sent('preference.unset')).toEqual({ key: 'send_mode.cold', reason: "the person's answer" });
        expect(checkedIn(cold)).toBe(false);
        expect(inGroup(cold).queryByRole('button', { name: 'Forget my answer' })).toBeNull();
    });

    it('says so when the preferences cannot be read, and offers nothing to save', async () => {
        const api = new FakeApi('Marta');
        api.unreachable('preference.list');
        mountSettings(api);
        expect(await screen.findByText('The send modes could not be read.')).toBeTruthy();
        expect(screen.queryByRole('button', { name: 'Save' })).toBeNull();
    });
});
