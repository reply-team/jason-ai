/**
 * What the chat tells the desktop's log: that a turn started and on which host, how it ended in numbers, how each
 * permission request was settled and by whom, once that the application carries no instruction text, and each time a
 * memory file of the chat folder could not be written, by its name and the system's error code. Never the
 * words of a turn, a tool's input, a command, a path, a session or a key: the host is an id from the desktop's own
 * tables, the file is the application's own name, the code is the system's (`EPERM`, say), and everything else is a
 * number, a boolean or an outcome.
 */

import { instructionsFile } from './chat-deny.ts';
import type { ChatOptions } from './chat.ts';
import type { Logger } from './log.ts';

export function loggedTurns(log: Logger): Required<Pick<ChatOptions, 'started' | 'ended' | 'decided' | 'instructionsMissing' | 'instructionsUnwritten'>> {
    return {
        started: (host, resumed) => log.info('chat_turn_started', { Host: host, Resumed: resumed }),
        ended: (turn) => log.info('chat_turn_ended', { Host: turn.host, Exit: turn.exit, DurationMs: turn.durationMs, Steps: turn.steps, Requests: turn.requests }),
        decided: (decided) => log.info('chat_request_decided', { Outcome: decided.outcome, By: decided.by }),
        instructionsMissing: () => log.warn('chat_instructions_missing', { File: instructionsFile }),
        instructionsUnwritten: (file, code) => log.warn('chat_instructions_unwritten', { File: file, Code: code }),
    };
}
