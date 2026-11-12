/**
 * A list that holds nothing yet: a sentence that says so and how it starts, and, where the page can open a chat (inside
 * the desktop), a button that opens a new one. In a browser the sentence stands alone.
 */
export function EmptyStart({ sentence, start }: { sentence: string; start: (() => void) | null }) {
    return (
        <div className="empty empty-start">
            <p>{sentence}</p>
            {start && (
                <button className="btn sm p" type="button" onClick={start}>
                    New chat
                </button>
            )}
        </div>
    );
}
