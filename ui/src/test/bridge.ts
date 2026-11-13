import type { StubDesktop } from './desktop';

// A stand-in for the desktop's bridge: a `fetch` that answers /pair, /call and /events as the bridge does, over a stub
// desktop, with no network. Its table is written out here on its own, as the bridge keeps its own, so a test of the
// page's table is not a test of itself.

export const standInSecret = 'planted-bridge-secret-Zx3_Cv5Bn7Mq9Wr1Ty3';
export const standInCode = 'planted-code-Ab1_Cd2Ef3Gh4';

const table = new Set([
    'mode.get',
    'theme.get',
    'fullAccess.standing',
    'setup.status',
    'chat.list',
    'chat.open',
    'chat.create',
    'chat.send',
    'chat.stop',
    'chat.rename',
]);

/** What the bridge answers a member outside its table. Held in one place until the bridge's own answer is known. */
const outsideTheTable = () => new Response('', { status: 403 });

export interface StandIn {
    fetch: typeof fetch;
    /** What was asked, in order: the path, and the member of a call. */
    asked: { path: string; member?: string; args?: unknown[]; origin?: string | null }[];
    /** Sends a line down the event stream. */
    push(line: unknown): void;
    /** Ends the event stream from the bridge's side. */
    end(): void;
    /** Makes the bridge forget the secret, as a closed desktop does. */
    forget(): void;
    /** Whether the code is spent: a second pairing with it fails. */
    spent: boolean;
}

export function standInBridge(desktop: StubDesktop, port = 43210): StandIn {
    let controller: ReadableStreamDefaultController<Uint8Array> | null = null;
    const encoder = new TextEncoder();
    const state: StandIn = {
        asked: [],
        spent: false,
        push: (line) => controller?.enqueue(encoder.encode(JSON.stringify(line) + '\n')),
        end: () => controller?.close(),
        forget: () => {
            known = false;
        },
        fetch: undefined as never,
    };
    let known = true;
    desktop.chat.onEvent((id, event) => state.push({ channel: 'chat.event', args: [id, event] }));
    desktop.setup.onChanged(() => state.push({ channel: 'setup.changed', args: [] }));
    desktop.fullAccess.onChanged((standing) => state.push({ channel: 'fullAccess.changed', args: [standing] }));
    state.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = new URL(String(input));
        if (url.host !== `127.0.0.1:${port}`) throw new Error('unreachable');
        const body = typeof init?.body === 'string' ? (JSON.parse(init.body) as Record<string, unknown>) : {};
        if (url.pathname === '/pair') {
            state.asked.push({ path: '/pair' });
            if (state.spent || body['code'] !== standInCode) return new Response('', { status: 401 });
            state.spent = true;
            return Response.json({ secret: standInSecret });
        }
        const headers = new Headers(init?.headers);
        if (!known || headers.get('authorization') !== `Bearer ${standInSecret}`) {
            state.asked.push({ path: url.pathname, member: '(not known)' });
            return new Response('', { status: 401 });
        }
        if (url.pathname === '/events') {
            state.asked.push({ path: '/events' });
            const stream = new ReadableStream<Uint8Array>({
                start: (made) => {
                    controller = made;
                    state.push({ heartbeat: true });
                },
            });
            return new Response(stream, { status: 200, headers: { 'content-type': 'application/x-ndjson' } });
        }
        if (typeof body['member'] !== 'string' || !Array.isArray(body['args']))
            return new Response('', { status: 400 });
        const member = body['member'];
        const args = (body['args'] as unknown[]) ?? [];
        state.asked.push({ path: '/call', member, args });
        if (!table.has(member)) return outsideTheTable();
        const [part, name] = member.split('.') as [string, string];
        const target = (desktop as unknown as Record<string, Record<string, (...a: unknown[]) => unknown>>)[part]![
            name
        ]!;
        const result = await target.apply(desktop[part as 'chat'], args);
        return Response.json({ result: result ?? null });
    }) as typeof fetch;
    return state;
}
