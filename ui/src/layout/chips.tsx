import { number } from '../time/format';

export type Chip<T> = { key: T; label: string; title?: string; count?: number | null };

/**
 * A row of choices, one of them on. A timeline's row is pills naming what it shows; a list's filters are square chips
 * with the number each counts.
 */
export function Chips<T extends string>({
    label,
    row,
    chips,
    on,
    onChoose,
}: {
    label: string;
    row: 'timeline' | 'filters';
    chips: readonly Chip<T>[];
    on: T;
    onChoose: (key: T) => void;
}) {
    const buttons = chips.map((chip) => (
        <button
            key={chip.key}
            type="button"
            title={chip.title}
            class={`chip${chip.key === on ? ' on' : ''}`}
            aria-pressed={chip.key === on}
            onClick={() => onChoose(chip.key)}
        >
            {chip.label}
            {chip.count != null && <span class="n"> {number.format(chip.count)}</span>}
        </button>
    ));
    return row === 'timeline' ? (
        <div class="chips" role="group" aria-label={label}>
            {buttons}
        </div>
    ) : (
        <div class="pfilters">{buttons}</div>
    );
}
