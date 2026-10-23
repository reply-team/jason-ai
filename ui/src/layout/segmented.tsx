import { number } from '../time/format';

export type Option<T> = { value: T; label: string; count?: number | null };

/** A row of square buttons, one of them on: the switch beside a title, and the choices in a filters bar. */
export function Segmented<T>({
    label,
    options,
    value,
    onChange,
}: {
    label: string;
    options: readonly Option<T>[];
    value: T;
    onChange: (value: T) => void;
}) {
    return (
        <div class="segmented" role="group" aria-label={label}>
            {options.map((option) => (
                <button
                    key={option.label}
                    type="button"
                    class={option.value === value ? 'on' : ''}
                    aria-pressed={option.value === value}
                    onClick={() => {
                        if (option.value !== value) onChange(option.value);
                    }}
                >
                    {option.label}
                    {option.count != null && <span class="count"> {number.format(option.count)}</span>}
                </button>
            ))}
        </div>
    );
}
