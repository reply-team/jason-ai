import type { CampaignActivityTotal } from '../../api/types';
import type { ActivityCount } from '../../router';
import { number } from '../../time/format';
import { counts, shareOfContacted } from './labels';

/**
 * The period's seven counts; each opens its people under the tiles, and the one open is marked. While the numbers are on
 * their way, the tiles keep their places, empty.
 */
export function Tiles({
    total,
    chosen,
    onChoose,
}: {
    total: CampaignActivityTotal | null;
    chosen: ActivityCount | undefined;
    onChoose: (count: ActivityCount) => void;
}) {
    return (
        <div class="tiles" role="group" aria-label="The period’s counts" aria-busy={total === null}>
            {counts.map(({ key, label, hint }) => {
                const value = total?.[key] ?? null;
                const on = chosen === key;
                const share =
                    total && value !== null && (key === 'replied' || key === 'opted_out')
                        ? shareOfContacted(value, total.contacted)
                        : null;
                return (
                    <button
                        key={key}
                        type="button"
                        class={`tile${on ? ' on' : ''}`}
                        aria-pressed={on}
                        aria-label={value === null ? label : `${label}, ${number.format(value)}`}
                        disabled={value === null}
                        title={hint}
                        onClick={() => onChoose(key)}
                    >
                        <b class={key === 'replied' ? 'hot' : undefined}>
                            {value === null ? '…' : number.format(value)}
                        </b>
                        <span class="label">{label}</span>
                        {share && <span class="rel">{share}</span>}
                    </button>
                );
            })}
        </div>
    );
}
