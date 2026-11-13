import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { standInBridge, standInCode, standInSecret } from '../test/bridge';
import { stubDesktop } from '../test/desktop';
import { Session, splitFragment } from './session';

// The tab's bootstrap: the desktop's code `d=<port>.<code>` read from the fragment with the ticket and wiped with it,
// traded once at the bridge, the pair kept in this tab's sessionStorage, and a kept pair tried once.

const door = {
    token: 'planted-ui-token-Qw3_Er5Ty7Ui9Op1As3Df5Gh7Jk9Lz1Xc3Vb5Nm7',
    token_id: 'uit_01JD0000000000000000000002',
    actor_id: 'ada',
    api_version: 'v1',
    instance_id: 'ins_01JD0000000000000000000001',
};
const code = `43210.${standInCode}`;

/** Answers the runtime's door and, for the bridge's port, the stand-in. */
function world() {
    const bridge = standInBridge(stubDesktop());
    const fetched = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        if (String(input) === '/ui/session') return Response.json(door);
        if (String(input).startsWith('http://127.0.0.1:43210')) return bridge.fetch(input, init);
        return new Response('{}', { status: 200 });
    });
    vi.stubGlobal('fetch', fetched);
    return { bridge, fetched };
}

beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    history.replaceState(null, '', '/ui/');
});
afterEach(() => {
    vi.unstubAllGlobals();
    sessionStorage.clear();
    localStorage.clear();
});

describe('the fragment', () => {
    it('splits the ticket and the desktop code out of the query, keeping the rest', () => {
        expect(splitFragment(`#/chat?t=tkt&d=${code}&x=1`)).toEqual({
            ticket: 'tkt',
            bridge: { port: 43210, code: standInCode },
            hash: '#/chat?x=1',
        });
        expect(splitFragment(`#/chat?d=${code}`)).toEqual({
            ticket: null,
            bridge: { port: 43210, code: standInCode },
            hash: '#/chat',
        });
        expect(splitFragment('#/chat')).toEqual({ ticket: null, bridge: null, hash: '#/chat' });
    });

    it('does not take a code that is not a port and a code', () => {
        for (const bad of [
            '0.abcdefghij',
            '70000.abcdefghij',
            '43210',
            '43210.short',
            'x.abcdefghij',
            '43210.a b c d e f g h',
        ])
            expect(splitFragment(`#/chat?d=${bad}`).bridge).toBeNull();
        // It is still taken out of the address.
        expect(splitFragment('#/chat?d=junk').hash).toBe('#/chat');
    });
});

describe('the tab opened by the desktop', () => {
    it('reads d= with the ticket, wipes both from the address, and pairs the bridge once', async () => {
        const { bridge } = world();
        history.replaceState(null, '', `/ui/#/chat?t=ticket-1&d=${code}`);
        const session = new Session();
        await session.bootstrap();
        expect(location.hash).toBe('#/chat');
        expect(location.href).not.toContain(standInCode);
        expect(session.token.value).toBe(door.token);
        expect(session.bridge.value).not.toBeNull();
        expect(bridge.asked.filter((each) => each.path === '/pair')).toHaveLength(1);
    });

    it('keeps the port and the secret in sessionStorage, and nothing of them in localStorage', async () => {
        world();
        history.replaceState(null, '', `/ui/#/chat?t=ticket-1&d=${code}`);
        await new Session().bootstrap();
        expect(JSON.parse(sessionStorage.getItem('jason.ui.bridge')!)).toEqual({ port: 43210, secret: standInSecret });
        expect(JSON.stringify({ ...localStorage })).not.toContain(standInSecret);
    });

    it('wipes d= from the address and holds no bridge when the bridge refuses the code', async () => {
        world();
        history.replaceState(null, '', `/ui/#/chat?t=ticket-1&d=43210.wrong-code-Ab1_Cd2`);
        const session = new Session();
        await session.bootstrap();
        expect(location.hash).toBe('#/chat');
        expect(session.bridge.value).toBeNull();
        expect(sessionStorage.getItem('jason.ui.bridge')).toBeNull();
    });

    it('holds no bridge when the desktop is not there', async () => {
        vi.stubGlobal(
            'fetch',
            vi.fn(async (input: RequestInfo | URL) => {
                if (String(input) === '/ui/session') return Response.json(door);
                throw new Error('unreachable');
            }),
        );
        history.replaceState(null, '', `/ui/#/chat?t=ticket-1&d=${code}`);
        const session = new Session();
        await session.bootstrap();
        expect(session.bridge.value).toBeNull();
    });
});

describe('a later load of the same tab', () => {
    it('tries the kept pair once and holds the bridge when it answers', async () => {
        const { bridge } = world();
        sessionStorage.setItem('jason.ui.bridge', JSON.stringify({ port: 43210, secret: standInSecret }));
        const session = new Session();
        await session.bootstrap();
        expect(session.bridge.value).not.toBeNull();
        expect(bridge.asked.filter((each) => each.path === '/call')).toHaveLength(1);
        expect(bridge.asked.some((each) => each.path === '/pair')).toBe(false);
    });

    it('drops the kept pair when the bridge does not take it, and tries nothing again', async () => {
        const { bridge } = world();
        bridge.forget();
        sessionStorage.setItem('jason.ui.bridge', JSON.stringify({ port: 43210, secret: standInSecret }));
        const session = new Session();
        await session.bootstrap();
        expect(session.bridge.value).toBeNull();
        expect(sessionStorage.getItem('jason.ui.bridge')).toBeNull();
        expect(bridge.asked.filter((each) => each.path === '/call')).toHaveLength(1);
        await new Session().bootstrap();
        expect(bridge.asked.filter((each) => each.path === '/call')).toHaveLength(1);
    });

    it('drops a kept pair that is not a port and a secret, without asking anyone', async () => {
        const { bridge } = world();
        sessionStorage.setItem('jason.ui.bridge', '{"port":"x"}');
        const session = new Session();
        await session.bootstrap();
        expect(session.bridge.value).toBeNull();
        expect(bridge.asked).toEqual([]);
    });

    it('lets go of the bridge when it later says it no longer knows the secret', async () => {
        const { bridge } = world();
        sessionStorage.setItem('jason.ui.bridge', JSON.stringify({ port: 43210, secret: standInSecret }));
        const session = new Session();
        await session.bootstrap();
        bridge.forget();
        await expect(session.bridge.value!.call('chat.list')).rejects.toThrow();
        expect(session.bridge.value).toBeNull();
        expect(sessionStorage.getItem('jason.ui.bridge')).toBeNull();
    });
});
