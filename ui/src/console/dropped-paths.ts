import { useLayoutEffect, useRef, useState, type DragEvent, type RefObject } from 'react';
import { can } from '../platform/places';
import { noPathDropped, somePathsDropped, withPaths } from './chat-model';
import { here } from './state';

// A file dropped on a box a message is written in, the chat's and Start's: its path goes in, quoted, at the caret, as a
// terminal does; several files, several paths. The page never goes to a dropped file, and a path it could not read is
// said, never passed over.

/** Whether a drag carries files from the system, rather than words dragged from a page. */
const carriesFiles = (transfer: DataTransfer | null): transfer is DataTransfer =>
    transfer !== null && [...transfer.types].includes('Files');

/** Where a dropped file is on this computer, as the desktop reads it; empty where this place, or the file, has none. */
function pathOfDropped(file: File): string {
    if (!can('chat.pathOf')) return '';
    // A desktop of an earlier version has no such member, and a file it cannot place throws: neither has a path here.
    try {
        return here().desktop.chat.pathOf(file);
    } catch {
        return '';
    }
}

/** What a box needs to take dropped files, and what it says when a path could not be put in. */
export interface DroppedPaths {
    /** The box's sentence about the last drop, until the next drop or a word typed (`typed`); null when there is none. */
    said: string | null;
    typed: () => void;
    draggedOver: (event: DragEvent<HTMLTextAreaElement>) => void;
    dropped: (event: DragEvent<HTMLTextAreaElement>) => void;
}

/**
 * Lets the box `box` take dropped files: `put` is given the box's text with the paths in, and the caret goes after them
 * once `text`, the box's text as drawn, shows them.
 */
export function useDroppedPaths(
    box: RefObject<HTMLTextAreaElement | null>,
    text: string,
    put: (next: string) => void,
): DroppedPaths {
    const [said, setSaid] = useState<string | null>(null);
    const caret = useRef<number | null>(null);
    useLayoutEffect(() => {
        const at = caret.current;
        if (at === null) return;
        caret.current = null;
        box.current?.setSelectionRange(at, at);
    }, [box, text]);
    return {
        said,
        typed: () => setSaid(null),
        draggedOver: (event) => {
            if (!carriesFiles(event.dataTransfer)) return;
            event.preventDefault();
            event.dataTransfer.dropEffect = 'copy';
        },
        dropped: (event) => {
            if (!carriesFiles(event.dataTransfer)) return;
            event.preventDefault();
            const files = [...event.dataTransfer.files];
            const paths = files.map(pathOfDropped).filter((each) => each !== '');
            // A drag that carries no file it can read, none at all included, is said too.
            setSaid(
                files.length > 0 && paths.length === files.length
                    ? null
                    : paths.length === 0
                      ? noPathDropped
                      : somePathsDropped,
            );
            if (paths.length === 0) return;
            const target = event.currentTarget;
            const next = withPaths(target.value, target.selectionStart, target.selectionEnd, paths);
            caret.current = next.caret;
            put(next.text);
            target.focus();
        },
    };
}
