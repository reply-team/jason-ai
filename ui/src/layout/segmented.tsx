import { number } from '../time/format';

export type Option<T> = { value: T; label: string; count?: number | null };

/**
 * A row of square buttons, one of them on: the switch beside a title, and the choices in a filters bar. With `tick`
 * the one on also carries a tick, so it is told apart by more than its colour.
 */
export function Segmented<T>({
    label,
    options,
    value,
    onChange,
    tick = false,
}: {
    label: string;
    options: readonly Option<T>[];
    value: T;
    onChange: (value: T) => void;
    tick?: boolean;
}) {
    return (
        <div className="segmented" role="group" aria-label={label}>
            {options.map((option) => (
                <button
                    key={option.label}
                    type="button"
                    className={option.value === value ? 'on' : ''}
                    aria-pressed={option.value === value}
                    onClick={() => {
                        if (option.value !== value) onChange(option.value);
                    }}
                >
                    {tick && option.value === value && (
                        <span className="tick" aria-hidden="true">
                            ✓{' '}
                        </span>
                    )}
                    {option.label}
                    {option.count != null && <span className="count"> {number.format(option.count)}</span>}
                </button>
            ))}
        </div>
    );
}
