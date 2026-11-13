import { afterEach, describe, expect, it, vi } from 'vitest';
import { stubDesktop } from '../test/desktop';
import { Session } from './session';

// What the door answers a pairing: the token, its id, who the page acts as. Made-up values.
const door = {
    token: 'planted-ui-token-Qw3_Er5Ty7Ui9Op1As3Df5Gh7Jk9Lz1Xc3Vb5Nm7',
    token_id: 'uit_01JD0000000000000000000002',
    actor_id: 'ada',
    api_version: 'v1',
    instance_id: 'ins_01JD0000000000000000000001',
};

function answering(): ReturnType<typeof vi.fn> {
    const fetched = vi.fn(async (url: string) => {
        if (url === '/ui/session')
            return new Response(JSON.stringify(door), { status: 200, headers: { 'content-type': 'application/json' } });
        return new Response('{}', { status: 200 });
    });
    vi.stubGlobal('fetch', fetched);
    return fetched;
}

afterEach(() => {
    vi.unstubAllGlobals();
    delete window.jasonDesktop;
    localStorage.clear();
});

describe("the window's pairing", () => {
    it('tells the desktop the id of the token it now holds, and never the token', async () => {
        answering();
        const desktop = stubDesktop();
        window.jasonDesktop = desktop;
        const session = new Session();
        await session.pair('ticket-1');
        expect(session.token.value).toBe(door.token);
        const told = desktop.calls.filter((call) => call.name === 'pairing.paired');
        expect(told).toEqual([{ name: 'pairing.paired', args: [door.token_id] }]);
        expect(JSON.stringify(desktop.calls)).not.toContain(door.token);
    });

    it('pairs as before where the desktop offers no such member, or refuses it, and in a browser', async () => {
        answering();
        const older = stubDesktop() as unknown as Record<string, unknown>;
        delete older['pairing'];
        window.jasonDesktop = older as never;
        const first = new Session();
        await first.pair('ticket-1');
        expect(first.token.value).toBe(door.token);

        const refusing = stubDesktop();
        refusing.refuse.set('pairing.paired', 'That is not a token id.');
        window.jasonDesktop = refusing;
        const second = new Session();
        await second.pair('ticket-2');
        expect(second.token.value).toBe(door.token);

        delete window.jasonDesktop;
        const browser = new Session();
        await browser.pair('ticket-3');
        expect(browser.token.value).toBe(door.token);
    });

    it('tells the desktop nothing when the door refused the ticket', async () => {
        vi.stubGlobal(
            'fetch',
            vi.fn(async () => new Response('{}', { status: 401 })),
        );
        const desktop = stubDesktop();
        window.jasonDesktop = desktop;
        await new Session().pair('spent-ticket');
        expect(desktop.calls.filter((call) => call.name === 'pairing.paired')).toEqual([]);
    });
});
