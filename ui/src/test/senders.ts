import { fixtures, type FixtureName } from './fixtures';

/** What an approval card's sender line asks of the recorded message: its campaign's accounts on email, the team's. */
export const SENDER_ASKED = { campaign_id: 'cmp_1', owners: 'team', sender_channel: 'email', limit: 100 };

/** The sentence under a line that was read, as a person reads it. */
export const SENDER_WHEN_WORDS = 'Read when this card was drawn; it is not part of what you approve.';

/** The wordings, each with the recorded answer that draws it. */
export const senderWordings: {
    name: string;
    answer: FixtureName;
    line: string;
    code?: string;
    message?: string;
    when: boolean;
}[] = [
    {
        name: 'the provider’s sending name for the mailbox',
        answer: 'sender.list/card-message-sender',
        line: 'Sending name at Reply now: Owner Example',
        when: true,
    },
    {
        name: 'that the provider holds no sending name for it',
        answer: 'sender.list/card-sender-unnamed',
        line: 'Reply holds no sending name for this mailbox.',
        when: true,
    },
    {
        name: 'that the provider does not list it now',
        answer: 'sender.list/card-sender-other',
        line: 'Reply does not list this mailbox now.',
        when: true,
    },
    {
        name: 'that the name could not be read, with the runtime’s code and words',
        answer: 'sender.list/card-sender-unread',
        line: 'The sending name could not be read. The runtime said:',
        code: 'no_route',
        message: fixtures['sender.list/card-sender-unread'].response.error.message,
        when: false,
    },
];
