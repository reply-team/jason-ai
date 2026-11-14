import { fireEvent, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';

// The link and the command a person can start a session with. The command line's own tests parse both commands through
// the program's parser and build both links from the table the click is read with, to the same text these hold.

function fake() {
    // An open card's reminder about name and tone reads the preferences; with both set it says nothing.
    return new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('approval.get', 'approval.get/pending-with-preview')
        .answer('decision.get', 'decision.get/pending-with-options')
        .answer('preference.list', 'preference.list/set');
}

/** The browser's own word for the machine, the newer reading where the browser has it and the older one where not. */
function fakePlatform(platform: string, newer: boolean) {
    if (newer) {
        Object.defineProperty(navigator, 'userAgentData', { configurable: true, value: { platform } });
        Object.defineProperty(navigator, 'platform', { configurable: true, value: 'should not be read' });
    } else {
        Object.defineProperty(navigator, 'platform', { configurable: true, value: platform });
    }
}

afterEach(() => {
    Reflect.deleteProperty(navigator, 'userAgentData');
    Reflect.deleteProperty(navigator, 'platform');
});

async function openApproval() {
    renderApp(fake());
    await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
    fireEvent.click(screen.getByText('Enroll Ada Lovelace'));
    await waitFor(() => screen.getByRole('button', { name: 'Approve' }));
}

async function openQuestion() {
    renderApp(fake());
    await waitFor(() => screen.getByText('Pause outreach to Vireo Labs until their funding news?'));
    fireEvent.click(screen.getByText('Pause outreach to Vireo Labs until their funding news?'));
    await waitFor(() => screen.getByRole('button', { name: 'Answer' }));
}

describe('Open an AI session', () => {
    it('links an open approval to its session on Windows, with the command beside it', async () => {
        fakePlatform('Windows', true);
        await openApproval();

        const link = screen.getByRole('link', { name: 'Open an AI session' });
        expect(link.getAttribute('href')).toBe('jason://attention/approvals/apr_1/session');
        expect(screen.getByText('jason session open --approval apr_1')).toBeTruthy();
        expect(screen.getByText('Run this in a terminal:')).toBeTruthy();
    });

    it('links an open question to its session on Windows, read from the older platform word', async () => {
        fakePlatform('Win32', false);
        await openQuestion();

        const link = screen.getByRole('link', { name: 'Open an AI session' });
        expect(link.getAttribute('href')).toBe('jason://attention/questions/dec_1/session');
        expect(screen.getByText('jason session open --question dec_1')).toBeTruthy();
    });

    it('shows only the command where no click can reach it', async () => {
        fakePlatform('macOS', true);
        await openApproval();

        expect(screen.queryByRole('link', { name: 'Open an AI session' })).toBeNull();
        expect(screen.getByText('jason session open --approval apr_1')).toBeTruthy();
    });

    it('shows only the command on Linux, read from the older platform word', async () => {
        fakePlatform('Linux x86_64', false);
        await openQuestion();

        expect(screen.queryByRole('link', { name: 'Open an AI session' })).toBeNull();
        expect(screen.getByText('jason session open --question dec_1')).toBeTruthy();
    });
});
