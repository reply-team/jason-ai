import { searchLines, type SearchWords } from '../../console/live';

/** A search's definition and arguments under its intent, line by line, as the runtime returned them. */
export function SearchLines({ search }: { search: SearchWords | null }) {
    if (search === null) return null;
    return (
        <div className="search-lines">
            {searchLines(search).map((line, at) => (
                <div key={at}>{line}</div>
            ))}
        </div>
    );
}
