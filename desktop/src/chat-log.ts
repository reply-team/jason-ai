/**
 * What the chat tells the desktop's log: that a turn started and on which host, how it ended in numbers, and how each
 * permission request was settled and by whom. Never the words of a turn, a tool's input, a command, a path, a session or
 * a key: the host is an id from the desktop's own tables, and everything else is a number, a boolean or an outcome.
 */

import type { ChatOptions } from './chat.ts';
import type { Logger } from './log.ts';

export function loggedTurns(log: Logger): Required<Pick<ChatOptions, 'started' | 'ended' | 'decided'>> {
    return {
        started: (host, resumed) => log.info('chat_turn_started', { Host: host, Resumed: resumed }),
        ended: (turn) => log.info('chat_turn_ended', { Host: turn.host, Exit: turn.exit, DurationMs: turn.durationMs, Steps: turn.steps, Requests: turn.requests }),
        decided: (decided) => log.info('chat_request_decided', { Outcome: decided.outcome, By: decided.by }),
    };
}
