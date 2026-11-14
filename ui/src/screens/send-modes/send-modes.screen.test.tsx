import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiProvider } from '../../api/context';
import type { Approval, Campaign } from '../../api/types';
import { ApprovalPanel } from '../attention/approval-panel';
import { campaign as open } from '../campaigns/store';
import { FakeApi } from '../../test/fake-api';
import { fixtures } from '../../test/fixtures';
import { AgentSendModes } from './agent-modes';
import { reminderKey } from './reminder';

// The recordings that carry a person's act were made by Marta.
const marta = () => new FakeApi('Marta');

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
        expect(api.calls).toEqual([]);

        fireEvent.click(radio('People who have not replied yet', 'Goes automatically'));
        expect(api.calls).toEqual([]);
        expect(screen.getByText(/say who writes and how in Settings, under "Your name and tone"/)).toBeTruthy();
        fireEvent.click(remember()[0]!);

        await waitFor(() =>
            expect(screen.getByText('Remembered. People who have not replied yet: Goes automatically.')).toBeTruthy(),
        );
        expect(api.sent('preference.set')).toEqual({
            key: 'send_mode.cold',
            value: { mode: 'automatic' },
            reason: "the person's answer",
        });
        expect(screen.queryByText(/say who writes and how in Settings/)).toBeNull();

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

        // Approving never waited on the question.
        fireEvent.click(screen.getByRole('button', { name: 'Approve' }));
        expect(onApprove).toHaveBeenCalledOnce();
    });

    it('asks only the kind that is still unset', () => {
        const approval = card('approval.get/pending-with-preview');
        approval.send_modes_unset = fixtures['approval.list/one-mode-unset'].response.items[0]!.send_modes_unset;
        mount(marta(), approval);

        expect(screen.getByRole('group', { name: 'People who have replied' })).toBeTruthy();
        expect(screen.queryByRole('group', { name: 'People who have not replied yet' })).toBeNull();
    });

    it('is not on a card whose modes are answered, and is not on a card already decided', () => {
        mount(marta().answer('preference.list', 'preference.list/set'), card('approval.get/modes-chosen'));
        expect(screen.queryByRole('heading', { name: 'How should messages go?' })).toBeNull();
        cleanup();
        const decided = card('approval.get/pending-with-preview');
        decided.status = 'approved';
        mount(marta(), decided);
        expect(screen.queryByRole('heading', { name: 'How should messages go?' })).toBeNull();
    });
});

describe('the quiet reminder about name and tone', () => {
    it('shows once the modes are answered and the name or tone is unset, and hides for seven days', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-11-14T10:00:00Z'));
        mount(marta().answer('preference.list', 'preference.list/none'), card('approval.get/modes-chosen'));

        const hide = await screen.findByRole('button', { name: 'Hide for 7 days' });
        expect(screen.getByText(/Your name and tone are not set\./)).toBeTruthy();
        expect(screen.getByRole('button', { name: 'Open Settings' })).toBeTruthy();
        fireEvent.click(hide);
        expect(screen.queryByText(/Your name and tone are not set\./)).toBeNull();
        expect(localStorage.getItem(reminderKey)).toBe(String(new Date('2026-11-21T10:00:00Z').getTime()));

        // Another card, six days on: still hidden, and the preferences are not even read.
        cleanup();
        vi.setSystemTime(new Date('2026-11-20T10:00:00Z'));
        const quiet = marta();
        mount(quiet, card('approval.get/modes-chosen'));
        expect(screen.queryByText(/Your name and tone are not set\./)).toBeNull();
        expect(quiet.calls).toEqual([]);

        // Eight days on it is shown again.
        cleanup();
        vi.setSystemTime(new Date('2026-11-22T10:00:00Z'));
        mount(marta().answer('preference.list', 'preference.list/none'), card('approval.get/modes-chosen'));
        await screen.findByText(/Your name and tone are not set\./);
    });

    it('is not shown when both are set', async () => {
        const api = marta().answer('preference.list', 'preference.list/set');
        mount(api, card('approval.get/modes-chosen'));
        await waitFor(() => expect(api.calls.map((call) => call.operation)).toEqual(['preference.list']));
        expect(screen.queryByText(/Your name and tone are not set\./)).toBeNull();
    });

    it('is shown, and hides, when this browser keeps nothing', async () => {
        const stored = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
            throw new Error('blocked');
        });
        const keeping = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
            throw new Error('blocked');
        });
        try {
            mount(marta().answer('preference.list', 'preference.list/none'), card('approval.get/modes-chosen'));
            fireEvent.click(await screen.findByRole('button', { name: 'Hide for 7 days' }));
            expect(screen.queryByText(/Your name and tone are not set\./)).toBeNull();
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
