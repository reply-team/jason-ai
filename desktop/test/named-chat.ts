// A test's own names for the chat's conversations. The page sends a turn only to a conversation the desktop made
// (`chat.create`) or kept; a test names its conversations instead ('c1', 'chat-a'), and the first send under a name makes
// that name's conversation, the campaign's when the send names one (`campaignId`, as `chat.create` takes it), else a
// set-up chat. Every later call under the name goes to that conversation, and its events come back under the name. A
// value that is no test name ('', '../x', 42) goes to the desktop as it is, so it is refused as any other would be.

import type { Chat } from '../src/chat.ts';

const testName = /^[a-z][a-z0-9-]{0,30}$/;

export class NamedChat {
    readonly #chat: () => Chat;
    readonly #ids = new Map<string, string>();
    readonly #names = new Map<string, string>();

    constructor(chat: () => Chat) {
        this.#chat = chat;
    }

    /** The desktop's id behind a name; anything else as it is. */
    idOf(name: unknown): unknown {
        return typeof name === 'string' ? (this.#ids.get(name) ?? name) : name;
    }

    /** The test's name for a conversation's id; an id no name stands for as it is. */
    nameOf(id: string): string {
        return this.#names.get(id) ?? id;
    }

    /** Makes the conversation behind a name on its first use, and resolves with its id. */
    async made(name: string, campaignId?: unknown): Promise<string> {
        const known = this.#ids.get(name);
        if (known !== undefined) return known;
        const made = await this.#chat().create(campaignId);
        this.#ids.set(name, made.id);
        this.#names.set(made.id, name);
        return made.id;
    }

    /**
     * Sends under a name. A name already made sends at once, as the page does, so a stop or a window close right after
     * it finds the turn the desktop started; a new name makes its conversation first.
     */
    send(name: unknown, text: unknown, options?: unknown): Promise<void> {
        let given = options;
        if (options !== null && typeof options === 'object' && 'campaignId' in options) {
            given = Object.fromEntries(Object.entries(options).filter(([key]) => key !== 'campaignId'));
        }
        if (typeof name === 'string' && testName.test(name) && !this.#ids.has(name)) {
            const campaignId = (options as { campaignId?: unknown } | null | undefined)?.campaignId;
            return this.made(name, campaignId).then(() => this.#chat().send(this.idOf(name), text, given));
        }
        return this.#chat().send(this.idOf(name), text, given);
    }

    stop(name: unknown): void {
        this.#chat().stop(this.idOf(name));
    }

    answer(name: unknown, request: unknown, decision: unknown): void {
        this.#chat().answer(this.idOf(name), request, decision);
    }

    isRunning(name: string): boolean {
        return this.#chat().isRunning(this.idOf(name) as string);
    }

    closeAll(): void {
        this.#chat().closeAll();
    }

    idle(): Promise<void> {
        return this.#chat().idle();
    }
}
