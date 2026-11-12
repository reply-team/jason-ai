// The gate a start holds while it puts the chat folder's skills right and deploys into it, so a turn sent meanwhile waits
// for the deploy instead of reading a skills folder that is half written. The start holds it from the pass over the chat
// folder's top to the last deploy; `Chat.send` waits on it before it builds a turn. A gate is not a lock between turns: it
// only makes a send wait while something holds it.

export class SkillsGate {
    #holds = 0;
    #waiting: (() => void)[] = [];

    /** Whether a start holds the gate now. */
    get held(): boolean {
        return this.#holds > 0;
    }

    /** Holds the gate; the function returned lets it go, once, however many times it is called. */
    hold(): () => void {
        this.#holds++;
        let released = false;
        return () => {
            if (released) return;
            released = true;
            if (--this.#holds === 0) for (const go of this.#waiting.splice(0)) go();
        };
    }

    /** Resolves at once while nothing holds the gate, otherwise when the last hold is let go. */
    wait(): Promise<void> {
        return this.#holds === 0 ? Promise.resolve() : new Promise<void>((resolve) => void this.#waiting.push(resolve));
    }
}
