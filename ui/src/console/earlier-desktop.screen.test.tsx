import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { session } from '../api/session';
import type { JasonDesktop } from '../platform/desktop';
import { followStanding, fullAccessStanding, readStandingAgain, takeBackStanding } from '../platform/full-access';
import { setPlace } from '../platform/places';
import { activatePlugins } from '../platform/plugin';
import { browserPlugins } from '../plugins';
import { route } from '../router';
import { chatDesktop, stubStream } from '../test/desktop';
import { FakeApi, watch } from '../test/fake-api';
import { Console } from './console';

// A desktop older than the page lacks parts of the object the page knows. The window's place can do everything, so the
// page also asks whether the part is there before it draws or calls it.

const without = (...parts: (keyof JasonDesktop)[]): JasonDesktop => {
    const desktop = chatDesktop() as unknown as Record<string, unknown>;
    for (const part of parts) delete desktop[part];
    return desktop as unknown as JasonDesktop;
};

afterEach(() => {
    cleanup();
    setPlace('window');
});

describe('A desktop without the Open in part', () => {
    it('draws the console with an empty Open in slot, and no error text', async () => {
        const desktop = without('openIn');
        const fake = new FakeApi()
            .answer('approval.list', 'approval.list/pending')
            .answer('decision.list', 'decision.list/pending')
            .answer('campaign.list', 'campaign.list/screen')
            .answer('campaign.summary', 'campaign.summary/all')
            .answer('system.info', 'system.info/with-ui');
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
        render(<Console api={fake} desktop={desktop} stream={stubStream().stream} />);
        await waitFor(() => screen.getByRole('region', { name: 'Mode bar' }));
        expect(document.querySelector('[data-slot="open-in"]')?.children).toHaveLength(0);
        expect(document.body.textContent).not.toMatch(/TypeError|undefined/);
    });
});

describe('A desktop without the Full access part', () => {
    it('reads, follows and takes back nothing, and says nothing is standing', async () => {
        const desktop = without('fullAccess');
        fullAccessStanding.value = { givenAt: '2026-11-11T09:00:00.000Z', newChats: true };
        const stop = followStanding(desktop);
        expect(fullAccessStanding.value).toBeNull();
        expect(() => readStandingAgain(desktop)).not.toThrow();
        expect(await takeBackStanding(desktop)).toBeNull();
        expect(() => stop()).not.toThrow();
    });
});
