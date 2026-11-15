import { doubtWords } from './model';

export { whyWords } from './model';

/**
 * Who doubted the send and why, on the card of the approval it parked: each doubt's reason in words, and the sentence
 * the doubter wrote as a text node, never as markup. Nothing is drawn where none stands.
 */
export function Doubts({ preview }: { preview: Record<string, unknown> }) {
    const drawn = doubtWords(preview);
    if (drawn === null) return null;
    return (
        <div className="section send-doubts">
            <h3>{drawn.heading}</h3>
            <ul>
                {drawn.doubts.map((doubt, index) => (
                    <li key={index}>
                        <p>{doubt.said}</p>
                        {doubt.note !== null && <p className="message-text">{doubt.note}</p>}
                    </li>
                ))}
            </ul>
        </div>
    );
}
