import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../app';
import { session } from '../../api/session';
import { consoleShell } from '../../platform/console-shell';
import { mode } from '../../platform/mode';
import { activatePlugins } from '../../platform/plugin';
import { browserPlugins } from '../../plugins';
import { firstCampaignTab, href, route, type Route } from '../../router';
import { FakeApi, watch } from '../../test/fake-api';
import { fixtures } from '../../test/fixtures';
import { CampaignChat } from './campaign-chat';

// Archiving an agent from its own Settings tab, in the console: one story's recordings (the `archive-` files), where
// Spring launch is linked to the provider's campaign c_7714 and holds four open work items (one a role processing now,
// one a search's stop, which the archive leaves to finish), an approval still asked, one already given on open work, one
// given to a send that has gone, and a question; Autumn outreach, a draft, stays in the list.

const recordedAt = new Date('2026-10-16T09:05:00Z');
const spring = fixtures['campaign.get/archive-spring'].response;
const list: Route = { screen: 'campaigns', tab: 'people', view: 'summary', period: '7d', filters: {} };
const settings: Route = { ...list, id: spring.id, tab: 'settings' };

beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(recordedAt);
    consoleShell.loaded = true;
    mode.value = 'live';
    firstCampaignTab.value = 'people';
});
afterEach(() => {
    vi.useRealTimers();
    consoleShell.loaded = false;
    mode.value = 'live';
    firstCampaignTab.value = 'sequence';
});

/** The page's reads before the archive, and the confirmation's. */
function before(): FakeApi {
    return new FakeApi()
        .answer('campaign.list', 'campaign.list/archive-before', 'campaign.list/archive-after')
        .answer(
            'campaign.summary',
            'campaign.summary/archive-before',
            'campaign.summary/archive-spring',
            'campaign.summary/archive-after',
        )
        .answer('campaign.get', 'campaign.get/archive-spring')
        .answer('workitem.list', 'workitem.list/archive-open')
        .answer('approval.list', 'approval.list/archive-asked-and-given')
        .answer('decision.list', 'decision.list/archive-pending')
        .answer('route.resolve', 'route.resolve/archive-pause');
}

/** The app paired and opened at an address, inside the desktop (with a chat to open) or in a browser tab (without). */
function open(api: FakeApi, to: Route, { chat = true }: { chat?: boolean } = {}) {
    watch(api);
    activatePlugins(browserPlugins);
    localStorage.clear();
    session.clear();
    localStorage.setItem(
        'jason.ui.session',
        JSON.stringify({ token: 'tok', token_id: 'uit_1', actor_id: 'ada', instance_id: 'rt_1' }),
    );
    api.answer('system.info', 'system.info/with-ui');
    location.hash = href(to);
    route.value = to;
    const app = <App api={api} uiApiVersion="v1" />;
    render(chat ? <CampaignChat.Provider value={() => {}}>{app}</CampaignChat.Provider> : app);
}

const sent = (api: FakeApi, operation: string) => api.calls.filter((c) => c.operation === operation).map((c) => c.body);
const written = (recorded: object) =>
    Object.fromEntries(Object.entries(recorded).filter(([key]) => key !== 'actor')) as Record<string, unknown>;

/** The Settings tab's last part, where the agent is archived. */
async function archivePart(): Promise<HTMLElement> {
    const heading = await screen.findByRole('heading', { level: 3, name: /^Archive/ });
    return heading.closest('.section') as HTMLElement;
}

async function confirmation(): Promise<HTMLElement> {
    fireEvent.click(within(await archivePart()).getByRole('button', { name: 'Archive' }));
    return screen.findByRole('dialog', { name: `Archive ${spring.name}` });
}

describe('Archiving an agent from its Settings', () => {
    it('is the last part of the Settings tab, one sentence and a button in the danger look', async () => {
        open(before(), settings);
        const part = await archivePart();

        expect(part.parentElement!.lastElementChild).toBe(part);
        expect(part.parentElement!.classList.contains('panel-body')).toBe(true);
        expect(part.textContent).toContain(
            'Archiving ends this agent for good and cannot be undone: its open work, the approvals on that work and its questions are cancelled.',
        );
        const button = within(part).getByRole('button', { name: 'Archive' });
        expect(button.className).toBe('btn danger');
        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('the confirmation names the agent, says it cannot be undone, and counts what it cancels as the runtime answers', async () => {
        const api = before();
        open(api, settings);
        const dialog = await confirmation();

        expect(within(dialog).getByRole('heading', { level: 2 }).textContent).toBe(`Archive ${spring.name}`);
        expect(dialog.textContent).toContain('An archive cannot be undone: nothing brings this agent back.');
        const cancels = await within(dialog).findByRole('region', { name: 'What archiving cancels' });
        expect(
            within(cancels)
                .getAllByRole('listitem')
                .map((each) => each.textContent),
        ).toEqual([
            '3 open work items, one of them processing now and stopped',
            '1 approval still asked of you',
            '1 approval already given, whose work then never runs',
            '1 question waiting for an answer',
        ]);
        // The search's stop is not among what is cancelled: the archive leaves it to run, and the confirmation says so.
        expect(cancels.textContent).toContain('1 search being stopped still finishes: the archive leaves it to run.');
        // The reads are the ones recorded: every open item, the approvals asked and given, the questions, and the pause's route.
        expect(sent(api, 'workitem.list')).toEqual([fixtures['workitem.list/archive-open'].request]);
        expect(sent(api, 'approval.list')).toEqual([fixtures['approval.list/archive-asked-and-given'].request]);
        expect(sent(api, 'decision.list')).toEqual([fixtures['decision.list/archive-pending'].request]);
        expect(sent(api, 'route.resolve')).toEqual([fixtures['route.resolve/archive-pause'].request]);
        expect(sent(api, 'campaign.archive')).toEqual([]);
    });

    it("says the provider's campaign is left sending, that Jason cannot pause it afterwards, and the way to stop it first", async () => {
        open(before(), settings);
        const dialog = await confirmation();

        const provider = await within(dialog).findByRole('region', { name: 'The campaign in Reply' });
        expect(provider.textContent).toContain(
            'The campaign in Reply (id c_7714) is left as it is: if it is live there, it keeps sending on its own schedule, and once this agent is archived Jason cannot pause it.',
        );
        expect(provider.textContent).toContain(
            "To stop it first, ask in this agent's chat to pause it in Reply, and archive once that work has finished; or pause it in Reply itself.",
        );
    });

    it("in a browser tab, where there is no chat to ask, the way to stop it first is the provider's own", async () => {
        open(before(), settings, { chat: false });
        const dialog = await confirmation();

        const provider = await within(dialog).findByRole('region', { name: 'The campaign in Reply' });
        expect(provider.textContent).toContain('To stop it first, pause it in Reply itself, then archive.');
        expect(provider.textContent).not.toContain('chat');
    });

    it('Archive sends one archive with the reason, however often it is pressed, then the list says what happened', async () => {
        const api = before().answer('campaign.archive', 'campaign.archive/archive-spring');
        open(api, settings);
        const dialog = await confirmation();
        await within(dialog).findByRole('region', { name: 'What archiving cancels' });
        fireEvent.input(within(dialog).getByRole('textbox', { name: 'Reason' }), {
            target: { value: 'The spring offer has ended.' },
        });

        const release = api.hold('campaign.archive');
        const confirm = within(dialog).getByRole('button', { name: 'Archive' });
        fireEvent.click(confirm);
        fireEvent.click(confirm);
        fireEvent.doubleClick(confirm);
        release();

        expect((await screen.findByText(new RegExp(`^${spring.name} is archived`))).textContent).toBe(
            `${spring.name} is archived: 3 open work items, 2 approvals and 1 question are cancelled.`,
        );
        expect(sent(api, 'campaign.archive')).toEqual([written(fixtures['campaign.archive/archive-spring'].request)]);
        // Back on the list, read again: the archived agent has left it, and no agent is open.
        await screen.findByText('Autumn outreach');
        await waitFor(() => expect(screen.queryByText(spring.name)).toBeNull());
        expect(screen.queryByRole('dialog')).toBeNull();
        expect(route.value).toMatchObject({ screen: 'campaigns' });
        expect((route.value as { id?: string }).id).toBeUndefined();
    });

    it('Archive waits for the reads, and Escape and Cancel leave the confirmation with nothing sent', async () => {
        const api = before();
        const release = api.hold('workitem.list');
        open(api, settings);
        let dialog = await confirmation();

        expect(dialog.textContent).toContain('Reading what this agent has open…');
        expect((within(dialog).getByRole('button', { name: 'Archive' }) as HTMLButtonElement).disabled).toBe(true);
        release();
        await within(dialog).findByRole('region', { name: 'What archiving cancels' });
        expect((within(dialog).getByRole('button', { name: 'Archive' }) as HTMLButtonElement).disabled).toBe(false);
        // The keyboard: the confirmation takes the focus as it opens, and Escape leaves it.
        await waitFor(() => expect(dialog.contains(document.activeElement)).toBe(true));
        fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());

        dialog = await confirmation();
        fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
        expect(sent(api, 'campaign.archive')).toEqual([]);
    });

    it('a reload with the confirmation open draws the Settings again with no confirmation and sends nothing', async () => {
        const api = before();
        open(api, settings);
        await confirmation();

        cleanup();
        open(api, settings);

        await archivePart();
        expect(screen.queryByRole('dialog')).toBeNull();
        expect(sent(api, 'campaign.archive')).toEqual([]);
    });

    it('Back with the confirmation open leaves it closed and sends nothing', async () => {
        const api = before();
        open(api, settings);
        await confirmation();

        // The address before it: the list, with no agent open.
        location.hash = href(list);
        route.value = list;

        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
        expect(sent(api, 'campaign.archive')).toEqual([]);
    });

    it('a deep link to an archived agent draws its record, says it is archived and offers no Archive', async () => {
        const api = new FakeApi()
            .answer('campaign.list', 'campaign.list/archive-after')
            .answer('campaign.summary', 'campaign.summary/archive-after', 'campaign.summary/archive-spring-archived')
            .answer('campaign.get', 'campaign.get/archive-spring-archived');
        open(api, settings);

        const part = await archivePart();
        expect(part.textContent).toContain('This agent is archived, and an archive cannot be undone.');
        expect(within(part).queryByRole('button')).toBeNull();
        expect(screen.getByText('Raw agent record')).toBeTruthy();
    });

    it('archived from a second window first, the confirm already open is answered with the agent as it is, and says so', async () => {
        const api = before().answer('campaign.archive', 'campaign.archive/archive-spring-again');
        open(api, settings);
        const dialog = await confirmation();
        await within(dialog).findByRole('region', { name: 'What archiving cancels' });
        fireEvent.input(within(dialog).getByRole('textbox', { name: 'Reason' }), {
            target: { value: 'The spring offer has ended.' },
        });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Archive' }));

        await screen.findByText(
            `${spring.name} is archived: 3 open work items, 2 approvals and 1 question are cancelled.`,
        );
        expect(sent(api, 'campaign.archive')).toEqual([
            written(fixtures['campaign.archive/archive-spring-again'].request),
        ]);
    });

    it('opened after a second window archived it, the confirmation says so, offers no Archive and counts nothing', async () => {
        const api = before().answer(
            'campaign.get',
            'campaign.get/archive-spring',
            'campaign.get/archive-spring-archived',
        );
        open(api, settings);
        await confirmation();

        // The answer opens a dialog of its own in place of the one that was reading.
        const said = await screen.findByText(/archived already/);
        const dialog = said.closest('[role="dialog"]') as HTMLElement;
        expect(said.textContent).toBe(
            'This agent is archived already, from another window or a terminal. Nothing more is done.',
        );
        expect(within(dialog).queryByRole('button', { name: 'Archive' })).toBeNull();
        // With no field to take it, the focus is on Close, so Escape leaves this one too.
        const close = within(dialog).getByRole('button', { name: 'Close' });
        await waitFor(() => expect(document.activeElement).toBe(close));
        fireEvent.keyDown(document.activeElement!, { key: 'Escape' });
        await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
        expect(sent(api, 'workitem.list')).toEqual([]);
        expect(sent(api, 'campaign.archive')).toEqual([]);
    });

    it('counts that cannot be read are said plainly, with why, that it still cannot be undone, and are read again', async () => {
        const api = before();
        const back = api.unreachable('workitem.list');
        open(api, settings);
        const dialog = await confirmation();

        const cancels = await within(dialog).findByRole('region', { name: 'What archiving cancels' });
        expect(Array.from(cancels.querySelectorAll('p'), (p) => p.textContent)).toEqual([
            'What this agent has open could not be read: the runtime did not answer.',
            'Archiving it still cancels its open work, the approvals on that work and its questions, and it cannot be undone.',
        ]);
        // The person may archive all the same: the facts that could not be read are said to be unread.
        expect((within(dialog).getByRole('button', { name: 'Archive' }) as HTMLButtonElement).disabled).toBe(false);
        // The provider's paragraph stands, read from the agent as it is.
        expect(within(dialog).getByRole('region', { name: 'The campaign in Reply' })).toBeTruthy();

        back();
        fireEvent.click(within(cancels).getByRole('button', { name: 'Read again' }));
        const counted = await within(dialog).findByText('Archiving cancels:');
        expect(counted.closest('section')!.textContent).toContain('3 open work items');
        expect(sent(api, 'workitem.list')).toEqual([
            fixtures['workitem.list/archive-open'].request,
            fixtures['workitem.list/archive-open'].request,
        ]);
        expect(sent(api, 'campaign.archive')).toEqual([]);
    });

    it("when the agent itself cannot be read, the provider's paragraph is said from the agent the page holds", async () => {
        const api = before();
        open(api, settings);
        await archivePart();
        // The page has read the agent; the confirmation's own read of it is not answered.
        api.unreachable('campaign.get');
        const dialog = await confirmation();

        const provider = await within(dialog).findByRole('region', { name: 'The campaign in Reply' });
        expect(provider.textContent).toContain(
            'The campaign in Reply (id c_7714) is left as it is: if it is live there, it keeps sending on its own schedule, and once this agent is archived Jason cannot pause it.',
        );
        // Whether Jason could pause it first was never read, so the way is the provider's own.
        expect(provider.textContent).toContain('To stop it first, pause it in Reply itself, then archive.');
        expect(within(dialog).getByRole('region', { name: 'What archiving cancels' }).textContent).toContain(
            'could not be read',
        );
        expect((within(dialog).getByRole('button', { name: 'Archive' }) as HTMLButtonElement).disabled).toBe(false);
        expect(sent(api, 'route.resolve')).toEqual([]);
    });

    it("a live agent whose route cannot pause the provider's campaign is offered the provider's own way only", async () => {
        const api = before().answer('route.resolve', 'route.resolve/archive-pause-unusable');
        open(api, settings);
        const dialog = await confirmation();

        const provider = await within(dialog).findByRole('region', { name: 'The campaign in Reply' });
        expect(provider.textContent).toContain('To stop it first, pause it in Reply itself, then archive.');
        expect(provider.textContent).not.toContain('chat');
        expect(sent(api, 'route.resolve')).toEqual([fixtures['route.resolve/archive-pause-unusable'].request]);
    });

    it("a live agent whose route for the pause cannot be read is offered the provider's own way only", async () => {
        const api = before();
        api.unreachable('route.resolve');
        open(api, settings);
        const dialog = await confirmation();

        const provider = await within(dialog).findByRole('region', { name: 'The campaign in Reply' });
        expect(provider.textContent).toContain('To stop it first, pause it in Reply itself, then archive.');
        expect(provider.textContent).not.toContain('chat');
        // The counts were read all the same.
        expect(within(dialog).getByText('Archiving cancels:')).toBeTruthy();
    });

    it('the classic shell draws the record alone, with no Archive', async () => {
        consoleShell.loaded = false;
        open(before(), settings);

        await screen.findByText('Raw agent record');
        expect(screen.queryByRole('heading', { level: 3, name: /^Archive/ })).toBeNull();
    });
});
