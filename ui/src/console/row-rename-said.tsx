import { useEffect } from 'react';
import { rowDrawn } from './recent';
import { dropUndrawnRename, rowRename } from './row-rename';

/**
 * The sidebar's one region for a row's rename refused: drawn once while the field is open and kept, so its words are announced when they change
 * and never again because the rows were painted; the words under the field are its visible copy. It also closes a
 * field whose row is no longer drawn, by a new list, a filter or a search.
 */
export function RowRenameSaid() {
    const at = rowRename.value;
    const drawn = at === null ? true : rowDrawn(at.id);
    useEffect(() => {
        if (!drawn) dropUndrawnRename(rowDrawn);
    }, [drawn]);
    // Drawn while a field is open, so a sidebar with none holds no empty alert.
    if (at === null) return null;
    return (
        <div className="rrename-live" role="alert">
            {at.said ?? ''}
        </div>
    );
}
