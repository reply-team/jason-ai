import type { Period } from '../router';
import { periodLabels } from '../time/periods';
import { Segmented } from './segmented';

const periods: readonly Period[] = ['today', '7d', '30d'];

/** Today, the last seven days or the last thirty, each ending now in the browser's time zone. */
export function PeriodSwitch({ value, onChange }: { value: Period; onChange: (period: Period) => void }) {
    return (
        <Segmented
            label="Period"
            options={periods.map((period) => ({ value: period, label: periodLabels[period] }))}
            value={value}
            onChange={onChange}
        />
    );
}
