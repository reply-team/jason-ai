import { useState } from 'react';
import { useApi } from '../../api/context';
import type { Approval, WorkItem } from '../../api/types';
import { Unpaired } from '../../api/client';
import { TextMoved, hasSubject, heldArgs, saveEdit, sentence, wordsOf, type Words } from './message-edit';

type Props = {
    approval: Approval;
    item: WorkItem;
    busy: boolean;
    onSaved: (item: WorkItem) => void;
    /** The item as it is now, when its text moved while the editor was open. */
    onMoved: (item: WorkItem) => void;
};

/** Edits the subject and the body of the parked message, and nothing else; Save approves nothing. */
export function MessageEditor({ approval, item, busy, onSaved, onMoved }: Props) {
    const api = useApi();
    const words = wordsOf(heldArgs(item), approval.operation);
    const [open, setOpen] = useState(false);
    const [subject, setSubject] = useState(words.subject ?? '');
    const [body, setBody] = useState(words.body ?? '');
    const [saving, setSaving] = useState(false);
    const [problem, setProblem] = useState<string | null>(null);
    const [moved, setMoved] = useState<Words | null>(null);
    // The text the editor opened with: a save is refused when the item no longer holds it.
    const [opened, setOpened] = useState<Words>(words);
    const withSubject = hasSubject(heldArgs(item), approval.operation);

    if (!open) {
        return (
            <button
                className="btn"
                type="button"
                disabled={busy}
                onClick={() => {
                    setSubject(words.subject ?? '');
                    setBody(words.body ?? '');
                    setProblem(null);
                    setMoved(null);
                    setOpened(words);
                    setOpen(true);
                }}
            >
                Edit message
            </button>
        );
    }

    async function save() {
        setSaving(true);
        setProblem(null);
        try {
            onSaved(await saveEdit(api, approval.operation, approval.work_item_id, subject, body, opened));
            setOpen(false);
        } catch (error) {
            if (error instanceof Unpaired) throw error;
            if (error instanceof TextMoved) {
                // What the person typed stays in the box; the new text is shown beside it and is what Save checks against now.
                const fresh = wordsOf(heldArgs(error.fresh), approval.operation);
                setMoved(fresh);
                setOpened(fresh);
                setProblem(error.message);
                onMoved(error.fresh);
                return;
            }
            // The box keeps what the person typed; the sentence says why it was not saved.
            setProblem(sentence(error));
        } finally {
            setSaving(false);
        }
    }

    return (
        <div className="message-editor">
            {withSubject && (
                <div className="field">
                    <label htmlFor={`edit-subject-${approval.id}`}>Subject</label>
                    <input
                        id={`edit-subject-${approval.id}`}
                        value={subject}
                        disabled={saving}
                        onChange={(e) => setSubject(e.target.value)}
                    />
                </div>
            )}
            <div className="field">
                <label htmlFor={`edit-body-${approval.id}`}>Body</label>
                <textarea
                    id={`edit-body-${approval.id}`}
                    rows={8}
                    value={body}
                    disabled={saving}
                    onChange={(e) => setBody(e.target.value)}
                />
            </div>
            {problem && (
                <p className="hint" role="alert">
                    {problem}
                </p>
            )}
            {moved && (
                <div className="message-card">
                    {moved.subject && <div className="message-text">{moved.subject}</div>}
                    {moved.body && <div className="message-text">{moved.body}</div>}
                </div>
            )}
            <div className="actions">
                <button className="btn primary" type="button" disabled={saving || busy} onClick={() => void save()}>
                    Save
                </button>
                <button className="btn ghost" type="button" disabled={saving} onClick={() => setOpen(false)}>
                    Cancel
                </button>
            </div>
        </div>
    );
}
