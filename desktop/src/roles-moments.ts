/**
 * The two moments the desktop puts the agents' roles on the AI the chat runs on (`Setup.connectRoles`), as `main.ts`
 * wires them: at a start once the runtime is up, with the host the chat is set to run on, and as each chat turn starts,
 * with the turn's own host. In Live alone: in the demo neither moment asks or reads anything, the chat's host included.
 * Nothing either moment meets stops the start or the turn; a failure is only told.
 */

import type { Mode } from './mode.ts';

export interface RolesMoments {
    /** The start, once the runtime is up and the window loaded. */
    atStart(): void;
    /** A chat turn's start, on its host. */
    atTurn(host: string): void;
}

export interface RolesMomentsDeps {
    connect(chosen: string | null): Promise<unknown>;
    /** The host the chat is set to run on, from what the chat already holds; asked at a Live start only. */
    chosen(): string | null;
    failed(): void;
}

export function rolesMoments(mode: Mode, deps: RolesMomentsDeps): RolesMoments {
    if (mode !== 'live') return { atStart: () => {}, atTurn: () => {} };
    const connect = (chosen: () => string | null) => {
        try {
            void deps.connect(chosen()).catch(() => deps.failed());
        } catch {
            deps.failed();
        }
    };
    return {
        atStart: () => connect(deps.chosen),
        atTurn: (host) => connect(() => host),
    };
}
