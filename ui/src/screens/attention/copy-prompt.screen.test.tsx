import { fireEvent, screen, waitFor } from '@testing-library/preact';
import { afterEach, describe, expect, it } from 'vitest';
import { FakeApi } from '../../test/fake-api';
import { renderApp } from '../../test/render-app';
// The published texts of what the button copies; the command line's own test writes them from the same recorded
// exchanges and holds them to what `--prompt` prints, so this holds the button to the command line.
import approvalText from '../../../../docs/prompts/approval.txt?raw';
import questionText from '../../../../docs/prompts/question.txt?raw';

/** The text as a person would paste it: the files are held with a line ending a checkout may change. */
const published = (text: string) => text.replace(/\r\n/g, '\n').replace(/\n$/, '');

function fakeClipboard(fail = false) {
    const written: string[] = [];
    Object.defineProperty(navigator, 'clipboard', {
        configurable: true,
        value: {
            writeText: (text: string) => {
                if (fail) return Promise.reject(new Error('denied'));
                written.push(text);
                return Promise.resolve();
            },
        },
    });
    return written;
}

function fake() {
    return new FakeApi()
        .answer('approval.list', 'approval.list/pending')
        .answer('decision.list', 'decision.list/pending')
        .answer('approval.get', 'approval.get/pending-with-preview')
        .answer('decision.get', 'decision.get/pending-with-options')
        .answer('campaign.get', 'campaign.get/in-project-with-folder');
}

afterEach(() => {
    Reflect.deleteProperty(navigator, 'clipboard');
});

describe('Copy prompt', () => {
    it("copies the published text for an open approval, with the campaign's folder", async () => {
        const written = fakeClipboard();
        renderApp(fake());
        await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
        fireEvent.click(screen.getByText('Enroll Ada Lovelace'));
        await waitFor(() => screen.getByRole('button', { name: 'Approve' }));

        fireEvent.click(screen.getByRole('button', { name: 'Copy prompt' }));

        await waitFor(() => expect(screen.getByRole('button', { name: 'Copied' })).toBeTruthy());
        expect(written).toEqual([published(approvalText)]);
    });

    it('copies the published text for an open question', async () => {
        const written = fakeClipboard();
        renderApp(fake());
        await waitFor(() => screen.getByText('Pause outreach to Vireo Labs until their funding news?'));
        fireEvent.click(screen.getByText('Pause outreach to Vireo Labs until their funding news?'));
        await waitFor(() => screen.getByRole('button', { name: 'Answer' }));

        fireEvent.click(screen.getByRole('button', { name: 'Copy prompt' }));

        await waitFor(() => expect(screen.getByRole('button', { name: 'Copied' })).toBeTruthy());
        expect(written).toEqual([published(questionText)]);
    });

    it('holds nothing of the item in what it copies', async () => {
        const written = fakeClipboard();
        renderApp(fake());
        await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
        fireEvent.click(screen.getByText('Enroll Ada Lovelace'));
        await waitFor(() => screen.getByRole('button', { name: 'Approve' }));
        fireEvent.click(screen.getByRole('button', { name: 'Copy prompt' }));
        await waitFor(() => expect(written.length).toBe(1));

        expect(written[0]).not.toContain('Ada Lovelace');
        expect(written[0]).not.toContain('campaign.enroll');
        expect(written[0]).not.toContain('Autumn outreach');
    });

    it('says so when the clipboard refuses', async () => {
        fakeClipboard(true);
        renderApp(fake());
        await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
        fireEvent.click(screen.getByText('Enroll Ada Lovelace'));
        await waitFor(() => screen.getByRole('button', { name: 'Approve' }));

        fireEvent.click(screen.getByRole('button', { name: 'Copy prompt' }));

        await waitFor(() => expect(screen.getByRole('button', { name: 'Copy failed' })).toBeTruthy());
    });
});
