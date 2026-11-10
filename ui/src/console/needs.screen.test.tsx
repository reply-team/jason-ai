import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { stubDesktop, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { fixtures } from '../test/fixtures';
import * as consoleCampaigns from './campaigns';
import { Console } from './console';
import { attention } from './needs';
import { chats, lines } from './state';

const hostile = '<img src=x onerror="window.hit=1">';

function renderConsole(fake: FakeApi) {
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
    fake.answer('system.info', 'system.info/with-ui')
        .answer('campaign.list', 'campaign.list/screen')
        .answer('campaign.summary', 'campaign.summary/all');
    render(<Console api={fake} desktop={stubDesktop()} stream={stubStream().stream} />);
}

const needs = () => screen.getByRole('heading', { name: 'Needs you', level: 1 }).closest<HTMLElement>('.painted')!;
const row = (id: string) => needs().querySelector<HTMLElement>(`[data-id="${id}"]`)!;
/** An element's text with its runs of white space made one space, as a person reads it. */
const words = (element: Element) => (element.textContent ?? '').replace(/\s+/g, ' ').trim();

beforeEach(() => {
    chats.value = {};
    lines.value = [];
    attention.loaded.value = false;
    attention.detail.value = null;
    attention.approvals.value = [];
    attention.questions.value = [];
    consoleCampaigns.campaigns.value = [];
    consoleCampaigns.read.value = false;
});

afterEach(() => {
    delete (window as { hit?: number }).hit;
});

describe('Needs you', () => {
    it('says on each row who the message goes to, from what the row carries and with no read per row', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/messages-pending')
            .answer('decision.list', 'decision.list/empty');
        renderConsole(fake);
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        expect(words(row('apr_1').querySelector('.needs-to')!)).toBe('to Ines Duarte');
        expect(words(row('apr_2').querySelector('.needs-to')!)).toBe('to Noor Haddad');
        expect(fake.calls.filter((each) => each.operation === 'approval.get')).toHaveLength(0);
    });

    it("shows a message's sender and its channel in words beside the recipient, subject and body", async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/messages-pending')
            .answer('decision.list', 'decision.list/empty')
            .answer('approval.get', 'approval.get/email-message', 'approval.get/linkedin-message');
        renderConsole(fake);
        await waitFor(() => expect(row('apr_1')).toBeTruthy());

        fireEvent.click(within(row('apr_1')).getByRole('button', { name: 'Details' }));
        await waitFor(() => within(row('apr_1')).getByText('Notes from the meetup'));
        const email = row('apr_1');
        expect(words(email.querySelector('.needs-from')!)).toBe('From owner@example.test');
        expect(words(email.querySelector('.needs-channel')!)).toBe('Channel Email');
        expect(within(email).getByText('To Ines Duarte, ines@example.test')).toBeTruthy();
        expect(within(email).getByText('Hi Ines, thanks for the talk at the meetup.')).toBeTruthy();

        fireEvent.click(within(row('apr_2')).getByRole('button', { name: 'Details' }));
        await waitFor(() => within(row('apr_2')).getByText('Thanks for connecting, Noor.'));
        const linkedIn = row('apr_2');
        expect(words(linkedIn.querySelector('.needs-from')!)).toBe('From https://www.linkedin.com/in/owner-example');
        expect(words(linkedIn.querySelector('.needs-channel')!)).toBe('Channel LinkedIn');
    });

    it('shows nothing in the place of a sender or a channel the preview does not carry', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending')
            .answer('decision.list', 'decision.list/empty')
            .answer('approval.get', 'approval.get/pending-with-preview');
        renderConsole(fake);
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        fireEvent.click(within(row('apr_1')).getByRole('button', { name: 'Details' }));
        await waitFor(() => within(row('apr_1')).getByText('Put Ada Lovelace into the campaign at the provider.'));
        expect(row('apr_1').querySelector('.needs-from')).toBeNull();
        expect(row('apr_1').querySelector('.needs-channel')).toBeNull();
        expect(words(row('apr_1'))).not.toMatch(/From|Channel|unknown/);
    });

    it('draws a hostile sender, channel and recipient as text', async () => {
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/messages-pending')
            .answer('decision.list', 'decision.list/empty');
        renderConsole(fake);
        await waitFor(() => expect(row('apr_1')).toBeTruthy());
        const recorded = fixtures['approval.get/email-message'].response;
        attention.approvals.value = attention.approvals.value.map((each) => ({ ...each, contact_name: hostile }));
        attention.detail.value = {
            kind: 'approvals',
            item: {
                ...recorded,
                preview: {
                    ...recorded.preview,
                    highlights: {
                        ...recorded.preview.highlights,
                        '/args/sender': { external_id: 's_12', address: hostile },
                        '/args/channel': hostile,
                    },
                },
            },
        };
        await waitFor(() => expect(within(row('apr_1')).getAllByText(hostile).length).toBe(3));
        expect(needs().querySelector('img')).toBeNull();
        expect((window as { hit?: number }).hit).toBeUndefined();
    });
});
