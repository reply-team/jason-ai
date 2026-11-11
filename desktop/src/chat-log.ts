/**
 * What the chat tells the desktop's log: that a turn started and on which host, how it ended in numbers, how each
 * permission request was settled and by whom, once that the application carries no instruction text, and each time a
 * memory file of the chat folder could not be written, by its name and the system's error code. Of the conversations:
 * how many were read at the start and how many files were not records, each record that could not be written by the
 * system's error code, how many new campaigns a set-up turn found and whether it handed over, and why a turn started a
 * fresh session; how each naming run of a set-up chat ended, as one closed word. Of a turn on Anthropic's key: the word the key source its host named was read as, when that word stops
 * nothing. Never the words of a turn, a tool's input, a command, a path, a session, a campaign, a conversation's
 * title or entries, or a key: the host is an id from the desktop's own tables, the file is the application's own name,
 * the code is the system's (`EPERM`, say), the reason one of two words, the key source one of three, a naming run's end one of eight, and everything else is a number, a boolean or an
 * outcome.
 */

import { instructionsFile } from './chat-deny.ts';
import type { ChatOptions } from './chat.ts';
import type { Logger } from './log.ts';
import type { ModelListsOptions } from './model-lists.ts';

type Logged = 'started' | 'ended' | 'decided' | 'instructionsMissing' | 'instructionsUnwritten' | 'conversationsRead' | 'conversationUnwritten' | 'setupEnded' | 'fresh' | 'keySource' | 'named';

export function loggedTurns(log: Logger): Required<Pick<ChatOptions, Logged>> {
    return {
        started: (host, resumed) => log.info('chat_turn_started', { Host: host, Resumed: resumed }),
        ended: (turn) => log.info('chat_turn_ended', { Host: turn.host, Exit: turn.exit, DurationMs: turn.durationMs, Steps: turn.steps, Requests: turn.requests }),
        decided: (decided) => log.info('chat_request_decided', { Outcome: decided.outcome, By: decided.by }),
        instructionsMissing: () => log.warn('chat_instructions_missing', { File: instructionsFile }),
        instructionsUnwritten: (file, code) => log.warn('chat_instructions_unwritten', { File: file, Code: code }),
        conversationsRead: (kept, unreadable) => log.info('chat_conversations_read', { Conversations: kept, Unreadable: unreadable }),
        conversationUnwritten: (code) => log.warn('chat_conversation_unwritten', { Code: code }),
        setupEnded: (found, handedOver) => log.info('chat_setup_ended', { ListRead: found !== null, Found: found ?? 0, HandedOver: handedOver }),
        fresh: (reason) => log.info('chat_turn_fresh', { Cause: reason }),
        keySource: (source) => log.warn('chat_key_source', { Source: source }),
        named: (outcome) => log.info('chat_named', { Outcome: outcome }),
    };
}

/**
 * What an ask of the models Codex offers tells the log: the host id it was asked under, from the desktop's own table,
 * and how many models it kept, or the kind of failure that kept the last list (`AskFailure`). Never a model's name or
 * what the host said.
 */
export function loggedModels(log: Logger): Required<Pick<ModelListsOptions, 'listed' | 'failed'>> {
    return {
        listed: (hostId, count) => log.info('chat_models_listed', { Host: hostId, Models: count }),
        failed: (hostId, kind) => log.warn('chat_models_unlisted', { Host: hostId, Kind: kind }),
    };
}
