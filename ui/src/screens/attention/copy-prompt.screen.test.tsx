import { fireEvent, screen, waitFor } from '@testing-library/preact';
import { afterEach, describe, expect, it, vi } from 'vitest';
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

/**
 * A clipboard that takes an item whose text is a promise, as a browser does: what it was handed is read after the
 * write was asked for, which is why a browser takes the write inside the click.
 */
function fakeItemClipboard() {
    const written: Promise<string>[] = [];
    class FakeClipboardItem {
        constructor(readonly items: Record<'text/plain', Promise<Blob>>) {}
    }
    vi.stubGlobal('ClipboardItem', FakeClipboardItem);
    Object.defineProperty(navigator, 'clipboard', {
        configurable: true,
        value: {
            write: (items: FakeClipboardItem[]) => {
                const item = items.at(0);
                if (!item) return Promise.reject(new Error('no item'));
                const text = item.items['text/plain'].then((blob) => blob.text());
                written.push(text);
                // As a browser's write does, it settles once the item's text has been read.
                return text.then(() => undefined);
            },
            writeText: () => Promise.reject(new Error('the fallback must not run where ClipboardItem is there')),
        },
    });
    return written;
}

afterEach(() => {
    Reflect.deleteProperty(navigator, 'clipboard');
    vi.unstubAllGlobals();
    vi.useRealTimers();
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

    it('asks the clipboard inside the click, before the folder is read, and the text is the published one', async () => {
        const written = fakeItemClipboard();
        const api = fake();
        renderApp(api);
        await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
        fireEvent.click(screen.getByText('Enroll Ada Lovelace'));
        await waitFor(() => screen.getByRole('button', { name: 'Approve' }));
        const release = api.hold('campaign.get');

        fireEvent.click(screen.getByRole('button', { name: 'Copy prompt' }));

        // The runtime has not answered the folder read, and the write is already asked for.
        await waitFor(() => expect(written.length).toBe(1));
        expect(screen.getByRole('button', { name: 'Copy prompt' }).hasAttribute('disabled')).toBe(true);
        release();
        await waitFor(() => expect(screen.getByRole('button', { name: 'Copied' })).toBeTruthy());
        expect(await written[0]).toBe(published(approvalText));
    });

    it("clears the first click's timer on a second, so Copied lasts its own time", async () => {
        fakeClipboard();
        renderApp(fake());
        await waitFor(() => screen.getByText('Enroll Ada Lovelace'));
        fireEvent.click(screen.getByText('Enroll Ada Lovelace'));
        await waitFor(() => screen.getByRole('button', { name: 'Approve' }));
        vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });

        fireEvent.click(screen.getByRole('button', { name: 'Copy prompt' }));
        await vi.advanceTimersByTimeAsync(10);
        expect(screen.getByRole('button', { name: 'Copied' })).toBeTruthy();
        await vi.advanceTimersByTimeAsync(2000);
        fireEvent.click(screen.getByRole('button', { name: 'Copied' }));
        await vi.advanceTimersByTimeAsync(1010);

        // 3020 ms after the first click: its own timer would have reset the label at 2510 ms.
        expect(screen.getByRole('button', { name: 'Copied' })).toBeTruthy();
    });
});
