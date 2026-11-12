/**
 * What the chat tells the desktop's log: that a turn started and on which host, how it ended in numbers, how each
 * permission request was settled and by whom, once that the application carries no instruction text, and each time a
 * memory file of the chat folder could not be written, by its name and the system's error code. Of the conversations:
 * how many were read at the start and how many files were not records, each record that could not be written by the
 * system's error code, how many new campaigns a set-up turn found and whether it handed over, and why a turn started a
 * fresh session; how each naming run of a set-up chat ended, as one closed word; each change of a conversation's mode, by
 * the two modes, and the mode of each turn started and of each request settled; a request Full access held, by the
 * literal reading's closed reason, or none. Each Windows check of Codex's sandbox, by its answer and whether the
 * desktop's settings file opened inside, a closed word each. Of a turn on Anthropic's key: the word the key source its host named was read as, when that word stops
 * nothing. Never the words of a turn, a tool's input, a command, a path, a session, a campaign, a conversation's
 * title or entries, or a key: the host is an id from the desktop's own tables, the file is the application's own name,
 * the code is the system's (`EPERM`, say), the reason one of two words, the key source one of three, a naming run's end one of eight, a mode one of three, and everything else is a number, a boolean or an
 * outcome.
 */

import { instructionsFile } from './chat-deny.ts';
import type { ChatOptions } from './chat.ts';
import type { Logger } from './log.ts';
import type { ModelListsOptions } from './model-lists.ts';

type Logged = 'started' | 'ended' | 'decided' | 'instructionsMissing' | 'instructionsUnwritten' | 'conversationsRead' | 'conversationUnwritten' | 'setupEnded' | 'fresh' | 'keySource' | 'named' | 'modeChanged' | 'unchecked' | 'fullFolderFailed' | 'sandboxChecked';

export function loggedTurns(log: Logger): Required<Pick<ChatOptions, Logged>> {
    return {
        started: (host, resumed, mode) => log.info('chat_turn_started', { Host: host, Resumed: resumed, Mode: mode }),
        ended: (turn) => log.info('chat_turn_ended', { Host: turn.host, Exit: turn.exit, DurationMs: turn.durationMs, Steps: turn.steps, Requests: turn.requests }),
        decided: (decided) =>
            decided.outcome === 'held'
                ? log.info('chat_request_held', { Outcome: decided.outcome, By: decided.by, Mode: decided.mode, Cause: decided.reason ?? 'none' })
                : log.info('chat_request_decided', { Outcome: decided.outcome, By: decided.by, Mode: decided.mode }),
        instructionsMissing: () => log.warn('chat_instructions_missing', { File: instructionsFile }),
        instructionsUnwritten: (file, code) => log.warn('chat_instructions_unwritten', { File: file, Code: code }),
        conversationsRead: (kept, unreadable) => log.info('chat_conversations_read', { Conversations: kept, Unreadable: unreadable }),
        conversationUnwritten: (code) => log.warn('chat_conversation_unwritten', { Code: code }),
        setupEnded: (found, handedOver) => log.info('chat_setup_ended', { ListRead: found !== null, Found: found ?? 0, HandedOver: handedOver }),
        fresh: (reason) => log.info('chat_turn_fresh', { Cause: reason }),
        keySource: (source) => log.warn('chat_key_source', { Source: source }),
        named: (outcome) => log.info('chat_named', { Outcome: outcome }),
        modeChanged: (from, to) => log.info('chat_mode_changed', { From: from, To: to }),
        unchecked: () => log.warn('chat_turn_stopped', { Cause: 'unchecked_tool' }),
        fullFolderFailed: (step, code) => log.warn('chat_full_folder_failed', { Step: step, Code: code }),
        sandboxChecked: (answer, settings) => log.info('chat_sandbox_checked', { Answer: answer, Settings: settings }),
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
