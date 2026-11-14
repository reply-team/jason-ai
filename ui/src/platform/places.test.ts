import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { declaredMembers } from '../../tests/guards/desktop-calls';
import { standInBridge, standInSecret } from '../test/bridge';
import { stubDesktop } from '../test/desktop';
import { Bridge, bridgeMembers, notInTab } from './bridge';
import {
    browserPlatform,
    can,
    cannotHere,
    canIn,
    currentDesktop,
    gated,
    noDesktop,
    placeKind,
    setPlace,
    tabDesktop,
    type DesktopMember,
} from './places';

// The places the console runs in are told apart by what each can do: the window everything, a tab with a bridge the
// bridge's table, a tab with no desktop nothing. The tab's object has the desktop's whole shape; what the table does not
// carry answers a refusal and sends nothing.

const members = declaredMembers(readFileSync(join(import.meta.dirname, 'desktop.d.ts'), 'utf8'));
const everyMember = [
    ...members.top,
    ...[...members.parts].flatMap(([part, inner]) => [...inner].map((each) => `${part}.${each}`)),
] as DesktopMember[];
const streamed = ['chat.onEvent', 'setup.onChanged', 'fullAccess.onChanged'];

const tabOver = () => {
    const stand = standInBridge(stubDesktop());
    const bridge = new Bridge({ port: 43210, secret: standInSecret }, stand.fetch);
    return { desktop: tabDesktop(bridge), stand, bridge };
};

const at = (object: unknown, member: string): ((...args: unknown[]) => unknown) =>
    member.split('.').reduce<unknown>((held, name) => (held as Record<string, unknown>)[name], object) as never;

afterEach(() => {
    setPlace('window');
    vi.unstubAllGlobals();
});

describe('what each place can do', () => {
    it('is everything in the window, the bridge table and its subscriptions in a tab, nothing with no desktop', () => {
        expect(everyMember.length).toBeGreaterThan(40);
        for (const member of everyMember) {
            expect(canIn('window', member)).toBe(true);
            expect(canIn('none', member)).toBe(false);
            expect(canIn('tab', member)).toBe(
                (bridgeMembers as readonly string[]).includes(member) || streamed.includes(member),
            );
        }
    });

    it('can do nothing until the page names its place', async () => {
        vi.resetModules();
        const fresh = await import('./places');
        expect(fresh.placeKind()).toBe('none');
        for (const member of everyMember) expect(fresh.can(member), member).toBe(false);
    });

    it('is asked of the place the page was started in', () => {
        setPlace('window');
        expect(placeKind()).toBe('window');
        expect(can('chat.remove')).toBe(true);
        setPlace('tab');
        expect(can('chat.remove')).toBe(false);
        expect(can('chat.send')).toBe(true);
        expect(cannotHere()).toBe("Done in Jason Desktop's own window.");
        setPlace('none');
        expect(can('chat.send')).toBe(false);
        expect(cannotHere()).toBe('This needs Jason Desktop.');
    });

    it('keeps an act from running where the place cannot do its member', () => {
        const act = vi.fn();
        setPlace('tab');
        gated('chat.remove', act)();
        expect(act).not.toHaveBeenCalled();
        gated('chat.send', act)();
        expect(act).toHaveBeenCalledTimes(1);
    });

    it('hands on the object the page was started with', () => {
        const { desktop } = tabOver();
        setPlace('tab', desktop);
        expect(currentDesktop()).toBe(desktop);
    });
});

describe('the Connect cards', () => {
    it('draw all their acts by one question, which holds while no act of setup is in the bridge table', () => {
        const acts = [...(members.parts.get('setup') ?? [])]
            .filter((each) => !['status', 'onChanged'].includes(each))
            .map((each) => `setup.${each}`);
        expect(acts.length).toBeGreaterThan(10);
        for (const act of acts) {
            expect(
                canIn('tab', act as DesktopMember),
                `${act} entered the bridge table: the Connect cards then need a question per control, not one for all`,
            ).toBe(false);
        }
        // The one question the cards ask stands for all of them.
        expect(canIn('tab', 'setup.connectReply')).toBe(false);
    });
});

describe('the object of a tab with a bridge', () => {
    it('has every member of the desktop, the whole shape', () => {
        const { desktop } = tabOver();
        for (const member of everyMember) expect(typeof at(desktop, member), member).toBe('function');
        expect(Object.keys(desktop).sort()).toEqual(['platform', ...members.top, ...members.parts.keys()].sort());
    });

    it('carries the members of the table over the bridge', async () => {
        const { desktop, stand } = tabOver();
        await desktop.mode.get();
        await desktop.theme.get();
        await desktop.fullAccess.standing();
        await desktop.setup.status();
        await desktop.chat.list();
        expect(stand.asked.filter((each) => each.path === '/call').map((each) => each.member)).toEqual([
            'mode.get',
            'theme.get',
            'fullAccess.standing',
            'setup.status',
            'chat.list',
        ]);
    });

    it('answers the refusal of its own type for every other member, and sends nothing', async () => {
        const { desktop, stand } = tabOver();
        const outside = everyMember.filter(
            (member) => !(bridgeMembers as readonly string[]).includes(member) && !streamed.includes(member),
        );
        expect(outside.length).toBeGreaterThan(25);
        for (const member of outside) {
            // A subscription or a read that has no answer to give is not a refusal; the rest say the one sentence.
            if (['onCommand', 'mode.onPreparing', 'links.onOpen'].includes(member)) {
                expect(typeof at(desktop, member)(() => undefined), member).toBe('function');
                continue;
            }
            if (member === 'links.take' || member === 'dailyTrends.get') {
                await expect(at(desktop, member)()).resolves.toBeNull();
                continue;
            }
            const said = await Promise.resolve(at(desktop, member)('a', 'b', 'c')).then(
                (answer) => (answer as { refused: string }).refused,
                (error: Error) => error.message,
            );
            expect(said, member).toBe(notInTab);
        }
        expect(stand.asked.filter((each) => each.path === '/call')).toEqual([]);
    });

    it('answers a reply act with a refusal of the reply shape', async () => {
        const { desktop } = tabOver();
        expect(await desktop.setup.replyPause('acc_1')).toEqual({
            refused: notInTab,
            code: null,
            offer: null,
            reread: false,
        });
    });

    it('takes its platform from the browser, never from the desktop', () => {
        const { desktop } = tabOver();
        vi.stubGlobal('navigator', { platform: 'MacIntel', userAgent: 'Mozilla/5.0 (Macintosh)' });
        expect(browserPlatform()).toBe('darwin');
        vi.stubGlobal('navigator', { platform: 'Win32', userAgent: 'Mozilla/5.0 (Windows NT 10.0)' });
        expect(browserPlatform()).toBe('win32');
        vi.stubGlobal('navigator', { platform: 'Linux x86_64', userAgent: 'Mozilla/5.0 (X11; Linux)' });
        expect(browserPlatform()).toBe('linux');
        expect(['darwin', 'win32', 'linux']).toContain(desktop.platform);
    });
});

describe('the object of a tab with no desktop', () => {
    it('has every member of the desktop, the whole shape', () => {
        const desktop = noDesktop();
        for (const member of everyMember) expect(typeof at(desktop, member), member).toBe('function');
        expect(Object.keys(desktop).sort()).toEqual(['platform', ...members.top, ...members.parts.keys()].sort());
    });

    it('answers what a page with no desktop can say: the own installation, no chats, nothing standing, no link', async () => {
        const desktop = noDesktop();
        expect(await desktop.mode.get()).toEqual({ mode: 'live', unopened: null, preparing: false });
        expect(await desktop.chat.list()).toEqual([]);
        expect(await desktop.fullAccess.standing()).toBeNull();
        expect(await desktop.theme.get()).toBeNull();
        expect(await desktop.links.take()).toBeNull();
        expect(await desktop.dailyTrends.get()).toBeNull();
    });

    it('refuses every act in the one sentence, and says nothing of what only the desktop knows', async () => {
        const desktop = noDesktop();
        const reads = ['mode.get', 'chat.list', 'fullAccess.standing', 'theme.get', 'links.take', 'dailyTrends.get'];
        const subscriptions = [
            'onCommand',
            'mode.onPreparing',
            'links.onOpen',
            'chat.onEvent',
            'setup.onChanged',
            'fullAccess.onChanged',
        ];
        const acts = everyMember.filter((member) => !reads.includes(member) && !subscriptions.includes(member));
        expect(acts.length).toBeGreaterThan(30);
        for (const member of acts) {
            const said = await Promise.resolve(at(desktop, member)('a', 'b', 'c')).then(
                (answer) => (answer as { refused: string }).refused,
                (error: Error) => error.message,
            );
            expect(said, member).toBe('This needs Jason Desktop.');
        }
        for (const member of subscriptions)
            expect(typeof at(desktop, member)(() => undefined), member).toBe('function');
    });

    it('can do nothing: the place named none asks no member of the desktop', () => {
        setPlace('none', noDesktop());
        for (const member of everyMember) expect(can(member), member).toBe(false);
    });
});
