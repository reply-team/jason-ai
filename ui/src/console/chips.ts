// The chips a chat offers: the opening three of an empty chat, and nothing else; no chat draws any under an answer. A
// click sends a chip's words as the person's next message and does nothing else, so every word a chip shows is one that
// acts on nothing (`passiveWords`). A chip shows the page's word for a campaign, "agent", and sends the runtime's,
// "campaign": the AI's own rules say campaign.

export interface Chip {
    shown: string;
    sent: string;
}

const chip = (shown: string, sent = shown): Chip => ({ shown, sent });

/** The chips of an empty chat; nothing else draws them. */
export const openingChips: readonly Chip[] = [
    chip('What is waiting for me?'),
    chip('How are my agents doing?', 'How are my campaigns doing?'),
    chip('Help me start a new agent.', 'Help me start a new campaign.'),
];

/** Every word a chip shows, once each: the words of buttons that act on nothing. */
export const chipWords: readonly string[] = [...new Set(openingChips.map((each) => each.shown))];
